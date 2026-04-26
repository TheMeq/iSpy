using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace iSpyApplication.Utilities
{
    internal static class RuntimeProfiler
    {
        private sealed class CameraStats
        {
            public string Name;
            public long Frames;
            public long Redraws;
            public int MaxBuffer;
            public int LastBuffer;
            public int Recording;
        }

        private static readonly ConcurrentDictionary<int, CameraStats> Cameras = new ConcurrentDictionary<int, CameraStats>();
        private static readonly bool EnabledFlag = string.Equals(Environment.GetEnvironmentVariable("ISPY_PROFILE"), "1", StringComparison.OrdinalIgnoreCase);
        private static DateTime _lastLog = DateTime.MinValue;
        private const int LogIntervalSeconds = 60;

        public static bool Enabled => EnabledFlag;

        public static void RecordCameraFrame(int id, string name, int bufferCount, bool recording)
        {
            if (!EnabledFlag)
                return;

            var stats = Cameras.GetOrAdd(id, _ => new CameraStats());
            stats.Name = name;
            Interlocked.Increment(ref stats.Frames);
            stats.LastBuffer = bufferCount;
            stats.Recording = recording ? 1 : 0;

            int current;
            while (bufferCount > (current = stats.MaxBuffer))
            {
                if (Interlocked.CompareExchange(ref stats.MaxBuffer, bufferCount, current) == current)
                    break;
            }
        }

        public static void RecordCameraRedraw(int id)
        {
            if (!EnabledFlag)
                return;

            var stats = Cameras.GetOrAdd(id, _ => new CameraStats());
            Interlocked.Increment(ref stats.Redraws);
        }

        public static void LogIfDue(double processCpu, double totalCpu, string counters)
        {
            if (!EnabledFlag || _lastLog > DateTime.UtcNow.AddSeconds(-LogIntervalSeconds))
                return;

            _lastLog = DateTime.UtcNow;
            var cameraStats = Cameras.OrderBy(p => p.Value.Name).Select(p =>
            {
                var stats = p.Value;
                var frames = Interlocked.Exchange(ref stats.Frames, 0);
                var redraws = Interlocked.Exchange(ref stats.Redraws, 0);
                var maxBuffer = Interlocked.Exchange(ref stats.MaxBuffer, stats.LastBuffer);
                return $"{stats.Name ?? p.Key.ToString()}: frames={frames}, redraws={redraws}, lastBuffer={stats.LastBuffer}, maxBuffer={maxBuffer}, recording={stats.Recording == 1}";
            });

            Logger.LogMessage($"PROFILE processCpu={processCpu:0.00}, totalCpu={totalCpu:0.00}, {counters}, cameras=[{string.Join("; ", cameraStats)}]");
        }
    }
}
