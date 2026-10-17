using System;
using System.Collections.Generic;

namespace Smpp
{
    // Insertion ordered retention: expiry and capacity eviction only visit removed entries.
    internal sealed class TimedCache<T>
    {
        private readonly Dictionary<string, LinkedListNode<(string Key, T Value, DateTime At)>> entries = new(StringComparer.Ordinal);
        private readonly LinkedList<(string Key, T Value, DateTime At)> order = new();
        internal int Count => entries.Count;
        internal bool ContainsKey(string key) => entries.ContainsKey(key);
        internal bool TryGetValue(string key, out T value)
        {
            if (entries.TryGetValue(key, out var node)) { value = node.Value.Value; return true; }
            value = default; return false;
        }
        internal void Set(string key, T value)
        {
            Remove(key, out _);
            entries.Add(key, order.AddLast((key, value, DateTime.UtcNow)));
        }
        internal bool Remove(string key, out T value)
        {
            if (entries.Remove(key, out var node))
            {
                order.Remove(node); value = node.Value.Value; return true;
            }
            value = default; return false;
        }
        internal void Trim(int capacity, DateTime cutoff, Action<T> removed = null)
        {
            while (order.First is { } first && (Count > capacity || first.Value.At <= cutoff))
            {
                Remove(first.Value.Key, out var value);
                removed?.Invoke(value);
            }
        }
        internal void Clear(Action<T> removed = null) => Trim(0, DateTime.MinValue, removed);
    }
}
