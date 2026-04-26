using System;
using System.Collections.Concurrent;
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
            public int Recording;
        }

        private static readonly ConcurrentDictionary<int, StreamStats> Cameras = new ConcurrentDictionary<int, StreamStats>();
        private static readonly ConcurrentDictionary<int, StreamStats> Microphones = new ConcurrentDictionary<int, StreamStats>();
        private static readonly bool EnabledFlag = string.Equals(Environment.GetEnvironmentVariable("ISPY_PROFILE"), "1", StringComparison.OrdinalIgnoreCase);
        private static DateTime _lastLog = DateTime.MinValue;
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
            if (!EnabledFlag || _lastLog > DateTime.UtcNow.AddSeconds(-LogIntervalSeconds))
                return;

            _lastLog = DateTime.UtcNow;
            var cameraStats = Cameras.OrderBy(p => p.Value.Name).Select(p => FormatStats(p.Key, p.Value));
            var microphoneStats = Microphones.OrderBy(p => p.Value.Name).Select(p => FormatStats(p.Key, p.Value));

            Logger.LogMessage($"PROFILE processCpu={processCpu:0.00}, totalCpu={totalCpu:0.00}, {counters}, cameras=[{string.Join("; ", cameraStats)}], microphones=[{string.Join("; ", microphoneStats)}]");
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
            return $"{stats.Name ?? id.ToString()}: frames={frames}, redraws={redraws}, lastBuffer={stats.LastBuffer}, maxBuffer={maxBuffer}, lastBufferBytes={stats.LastBufferBytes}, maxBufferBytes={maxBufferBytes}, dropped={stats.LastDropped}, recording={stats.Recording == 1}";
        }
    }
}
