using System;
using System.Threading;

namespace Smpp
{
    /// <summary>Bounded in-memory receipt correlation for one provider/account, optionally shared by TX and RX binds.</summary>
    public sealed class DeliveryTrackingStore
    {
        internal object SyncRoot { get; } = new();
        internal bool ExternallyAssigned;
        internal TimedCache<Connection.DeliveryGroup> Parts { get; } = new();
        internal TimedCache<Connection.EarlyReceipt> EarlyReceipts { get; } = new();
        internal TimedCache<bool> AmbiguousIds { get; } = new();
        private TimeSpan lifetime = TimeSpan.FromHours(24);
        private int capacity = 10000;
        private Timer expiryTimer;
        private long scheduledTicks;
        public TimeSpan Lifetime
        {
            get { lock (SyncRoot) return lifetime; }
            set
            {
                if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
                lock (SyncRoot) { lifetime = value; Expire(DateTime.UtcNow); }
            }
        }
        public int Capacity
        {
            get { lock (SyncRoot) return capacity; }
            set { Guard.ThrowIfNegativeOrZero(value); lock (SyncRoot) { capacity = value; Expire(DateTime.UtcNow); } }
        }

        internal void Expire(DateTime now)
        {
            lock (SyncRoot)
            {
                var cutoff = new DateTime(Math.Max(0, now.Ticks - lifetime.Ticks), DateTimeKind.Utc);
                Parts.Trim(capacity, cutoff, Connection.ExpireDeliveryGroup);
                EarlyReceipts.Trim(capacity, cutoff);
                AmbiguousIds.Trim(capacity, cutoff);
                ScheduleExpiry();
            }
        }

        // Called under SyncRoot after an insertion, removal or expiry. A store has one timer
        // while it retains entries, including during an outage with no active connection.
        internal void ScheduleExpiry()
        {
            long oldest = Math.Min(Parts.Oldest.Ticks, Math.Min(EarlyReceipts.Oldest.Ticks, AmbiguousIds.Oldest.Ticks));
            if (oldest == DateTime.MaxValue.Ticks)
            {
                expiryTimer?.Dispose();
                expiryTimer = null;
                scheduledTicks = 0;
                return;
            }
            long due = oldest + Math.Min(lifetime.Ticks, DateTime.MaxValue.Ticks - oldest);
            if (scheduledTicks == due) return;
            scheduledTicks = due;
            var remaining = TimeSpan.FromTicks(due - DateTime.UtcNow.Ticks);
            int delay = (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(remaining.TotalMilliseconds)));
            if (expiryTimer == null)
            {
                if (ExecutionContext.IsFlowSuppressed()) expiryTimer = CreateTimer();
                else using (ExecutionContext.SuppressFlow()) expiryTimer = CreateTimer();
            }
            expiryTimer.Change(delay, Timeout.Infinite);
        }

        private Timer CreateTimer() => new Timer(_ =>
        {
            lock (SyncRoot) { scheduledTicks = 0; Expire(DateTime.UtcNow); }
        }, null, Timeout.Infinite, Timeout.Infinite);
        /// <summary>Expires all unfinished submissions. Coordinate with every connection using this store before clearing it.</summary>
        public void Clear()
        {
            lock (SyncRoot)
            {
                Parts.Clear(Connection.ExpireDeliveryGroup);
                EarlyReceipts.Clear();
                AmbiguousIds.Clear();
                ScheduleExpiry();
            }
        }
    }
}
