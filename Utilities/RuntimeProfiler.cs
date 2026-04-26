using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace iSpyApplication.Utilities
{
    internal static class RuntimeProfiler
    {
        private sealed class StreamStats
        {
            public string Name;
            public long Frames;
            public long Redraws;
            public int MaxBuffer;
            public int LastBuffer;
            public long MaxBufferBytes;
            public long LastBufferBytes;
            public long LastDropped;
            public long LastLoggedDropped;
            public int Recording;
        }

        private static readonly ConcurrentDictionary<int, StreamStats> Cameras = new ConcurrentDictionary<int, StreamStats>();
        private static readonly ConcurrentDictionary<int, StreamStats> Microphones = new ConcurrentDictionary<int, StreamStats>();
        private static readonly bool EnabledFlag = string.Equals(Environment.GetEnvironmentVariable("ISPY_PROFILE"), "1", StringComparison.OrdinalIgnoreCase);
        private static readonly string SessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static readonly DateTime StartUtc = DateTime.UtcNow;
        private static DateTime _lastLog = DateTime.MinValue;
        private static int _startupLogged;
        private const int LogIntervalSeconds = 60;

        public static bool Enabled => EnabledFlag;

        public static void RecordCameraFrame(int id, string name, int bufferCount, long bufferBytes, long dropped, bool recording)
        {
            if (!EnabledFlag)
                return;

            RecordFrame(Cameras, id, name, bufferCount, bufferBytes, dropped, recording);
        }

        public static void RecordMicrophoneFrame(int id, string name, int bufferCount, long bufferBytes, long dropped, bool recording)
        {
            if (!EnabledFlag)
                return;

            RecordFrame(Microphones, id, name, bufferCount, bufferBytes, dropped, recording);
        }

        public static void RecordCameraRedraw(int id)
        {
            if (!EnabledFlag)
                return;

            var stats = Cameras.GetOrAdd(id, _ => new StreamStats());
            Interlocked.Increment(ref stats.Redraws);
        }

        public static void LogIfDue(double processCpu, double totalCpu, string counters)
        {
            if (!EnabledFlag)
                return;

            if (Interlocked.Exchange(ref _startupLogged, 1) == 0)
            {
                Logger.LogMessage($"PROFILE startup session={SessionId}, enabled=ISPY_PROFILE=1, appVersion={typeof(MainForm).Assembly.GetName().Version}, bitness={(Environment.Is64BitProcess ? "x64" : "x86")}, os={Environment.OSVersion}, intervalSeconds={LogIntervalSeconds}");
            }

            if (_lastLog > DateTime.UtcNow.AddSeconds(-LogIntervalSeconds))
                return;

            _lastLog = DateTime.UtcNow;
            var cameraStats = Cameras.OrderBy(p => p.Value.Name).Select(p => FormatStats(p.Key, p.Value));
            var microphoneStats = Microphones.OrderBy(p => p.Value.Name).Select(p => FormatStats(p.Key, p.Value));
            var process = Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - StartUtc;
            var gcMemoryMb = GC.GetTotalMemory(false) / 1048576;

            Logger.LogMessage($"PROFILE session={SessionId}, uptime={uptime:hh\\:mm\\:ss}, processCpu={processCpu:0.00}, totalCpu={totalCpu:0.00}, {counters}, gcMemoryMb={gcMemoryMb}, gc0={GC.CollectionCount(0)}, gc1={GC.CollectionCount(1)}, gc2={GC.CollectionCount(2)}, threads={process.Threads.Count}, cameras=[{string.Join("; ", cameraStats)}], microphones=[{string.Join("; ", microphoneStats)}]");
        }

        private static void RecordFrame(ConcurrentDictionary<int, StreamStats> streams, int id, string name, int bufferCount, long bufferBytes, long dropped, bool recording)
        {
            var stats = streams.GetOrAdd(id, _ => new StreamStats());
            stats.Name = name;
            Interlocked.Increment(ref stats.Frames);
            stats.LastBuffer = bufferCount;
            stats.LastBufferBytes = bufferBytes;
            stats.LastDropped = dropped;
            stats.Recording = recording ? 1 : 0;

            int current;
            while (bufferCount > (current = stats.MaxBuffer))
            {
                if (Interlocked.CompareExchange(ref stats.MaxBuffer, bufferCount, current) == current)
                    break;
            }

            long currentBytes;
            while (bufferBytes > (currentBytes = stats.MaxBufferBytes))
            {
                if (Interlocked.CompareExchange(ref stats.MaxBufferBytes, bufferBytes, currentBytes) == currentBytes)
                    break;
            }
        }

        private static string FormatStats(int id, StreamStats stats)
        {
            var frames = Interlocked.Exchange(ref stats.Frames, 0);
            var redraws = Interlocked.Exchange(ref stats.Redraws, 0);
            var maxBuffer = Interlocked.Exchange(ref stats.MaxBuffer, stats.LastBuffer);
            var maxBufferBytes = Interlocked.Exchange(ref stats.MaxBufferBytes, stats.LastBufferBytes);
            var lastLoggedDropped = Interlocked.Exchange(ref stats.LastLoggedDropped, stats.LastDropped);
            var droppedDelta = stats.LastDropped - lastLoggedDropped;
            return $"{stats.Name ?? id.ToString()}#{id}: frames={frames}, redraws={redraws}, lastBuffer={stats.LastBuffer}, maxBuffer={maxBuffer}, lastBufferBytes={stats.LastBufferBytes}, maxBufferBytes={maxBufferBytes}, droppedTotal={stats.LastDropped}, droppedDelta={droppedDelta}, recording={stats.Recording == 1}";
        }
    }
}
