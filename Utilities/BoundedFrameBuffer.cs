using System;
using System.Collections.Concurrent;
using System.Threading;

namespace iSpyApplication.Utilities
{
    internal sealed class BoundedFrameBuffer : IDisposable
    {
        private readonly ConcurrentQueue<Helper.FrameAction> _queue = new ConcurrentQueue<Helper.FrameAction>();
        private readonly AutoResetEvent _available = new AutoResetEvent(false);
        private int _count;
        private long _bytes;
        private long _dropped;

        public int Count => Volatile.Read(ref _count);
        public long Bytes => Interlocked.Read(ref _bytes);
        public long Dropped => Interlocked.Read(ref _dropped);
        public WaitHandle AvailableWaitHandle => _available;

        public bool TryPeek(out Helper.FrameAction frameAction)
        {
            return _queue.TryPeek(out frameAction);
        }

        public bool TryDequeue(out Helper.FrameAction frameAction)
        {
            if (!_queue.TryDequeue(out frameAction))
                return false;

            Interlocked.Decrement(ref _count);
            Interlocked.Add(ref _bytes, -GetApproximateBytes(frameAction));
            if (Count > 0)
                _available.Set();
            return true;
        }

        public void Enqueue(Helper.FrameAction frameAction, DateTime? keepAfter, int maxItems, long maxBytes)
        {
            _queue.Enqueue(frameAction);
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _bytes, GetApproximateBytes(frameAction));
            _available.Set();
            Trim(keepAfter, maxItems, maxBytes);
        }

        public void Trim(DateTime? keepAfter, int maxItems, long maxBytes)
        {
            while (ShouldDropNext(keepAfter, maxItems, maxBytes))
            {
                Helper.FrameAction dropped;
                if (!TryDequeue(out dropped))
                    return;

                Interlocked.Increment(ref _dropped);
                dropped.Dispose();
            }
        }

        public void Clear()
        {
            Helper.FrameAction frameAction;
            while (TryDequeue(out frameAction))
            {
                frameAction.Dispose();
            }
            _available.Reset();
        }

        public void Dispose()
        {
            Clear();
            _available.Dispose();
        }

        private bool ShouldDropNext(DateTime? keepAfter, int maxItems, long maxBytes)
        {
            if (Count > maxItems || Bytes > maxBytes)
                return true;

            if (!keepAfter.HasValue)
                return false;

            Helper.FrameAction next;
            return _queue.TryPeek(out next) && next.TimeStamp < keepAfter.Value;
        }

        private static long GetApproximateBytes(Helper.FrameAction frameAction)
        {
            if (frameAction == null)
                return 0;

            if (frameAction.Frame != null)
                return (long)frameAction.Frame.Width * frameAction.Frame.Height * 4;

            return frameAction.DataLength > 0 ? frameAction.DataLength : frameAction.Content?.Length ?? 0;
        }
    }
}
