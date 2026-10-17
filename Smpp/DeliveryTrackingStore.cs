using System;

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
        public TimeSpan Lifetime
        {
            get { lock (SyncRoot) return lifetime; }
            set { if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value)); lock (SyncRoot) lifetime = value; }
        }
        public int Capacity
        {
            get { lock (SyncRoot) return capacity; }
            set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); lock (SyncRoot) capacity = value; }
        }
        /// <summary>Expires all unfinished submissions. Coordinate with every connection using this store before clearing it.</summary>
        public void Clear()
        {
            lock (SyncRoot)
            {
                Parts.Clear(Connection.ExpireDeliveryGroup);
                EarlyReceipts.Clear();
                AmbiguousIds.Clear();
            }
        }
    }
}
