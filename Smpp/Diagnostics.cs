using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Smpp
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Bound, Reconnecting, Stopped }
    public sealed record ConnectionStateChange(string ChannelName, ConnectionState State);
    public sealed record ProtocolDiagnostic(string ChannelName, string Name, uint? SequenceNumber = null,
        uint? CommandStatus = null, double? DurationMilliseconds = null);

    /// <summary>BCL instrumentation. Subscribe using MeterListener, ActivityListener, or an OpenTelemetry exporter.</summary>
    public static class SmppDiagnostics
    {
        public const string Name = "SMSGate";
        public static readonly Meter Meter = new(Name);
        public static readonly ActivitySource ActivitySource = new(Name);
        internal static readonly Counter<long> Sent = Meter.CreateCounter<long>("smpp.pdus.sent");
        internal static readonly Counter<long> Responses = Meter.CreateCounter<long>("smpp.responses");
        internal static readonly Counter<long> Receipts = Meter.CreateCounter<long>("smpp.delivery.receipts");
        internal static readonly UpDownCounter<long> Queue = Meter.CreateUpDownCounter<long>("smpp.queue.depth");
        internal static readonly UpDownCounter<long> InFlight = Meter.CreateUpDownCounter<long>("smpp.pdus.in_flight");
        internal static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("smpp.ack.duration", "ms");
        internal static readonly Histogram<double> DeliveryLatency = Meter.CreateHistogram<double>("smpp.delivery.duration", "ms");
        internal static readonly Counter<long> Delivered = Meter.CreateCounter<long>("smpp.delivery.completed");
    }
    public abstract partial class Connection
    {
        private ConnectionState state;
        public ConnectionState State { get { lock (SessionLock) return state; } }
        public string ChannelName => channel_name;
        public event Action<ConnectionStateChange> StateChanged;
        public event Action<ProtocolDiagnostic> Diagnostic;
        protected void ChangeState(ConnectionState value)
        {
            if (state == value) return;
            state = value;
            Notify(() => StateChanged?.Invoke(new ConnectionStateChange(channel_name, value)));
        }
        private void Diagnose(string name, uint? sequence = null, uint? status = null, double? elapsed = null)
        {
            var handler = Diagnostic;
            if (handler == null) return;
            Notify(() => handler(new ProtocolDiagnostic(channel_name, name, sequence, status, elapsed)));
        }

        // Meter listeners execute synchronously. Dispatch measurements like other application callbacks.
        private void Measure(Counter<long> instrument, long value, KeyValuePair<string, object>? tag = null)
        {
            if (!instrument.Enabled) return;
            Notify(() => { if (tag.HasValue) instrument.Add(value, tag.Value); else instrument.Add(value); });
        }
        private void Measure(UpDownCounter<long> instrument, long value)
        {
            if (instrument.Enabled) Notify(() => instrument.Add(value));
        }
        private void Measure(Histogram<double> instrument, double value)
        {
            if (instrument.Enabled) Notify(() => instrument.Record(value));
        }
        private static Task RunDetached(Func<Task> action)
        {
            if (ExecutionContext.IsFlowSuppressed()) return Task.Run(action);
            using (ExecutionContext.SuppressFlow()) return Task.Run(action);
        }
        private static Task RunDetached(Action action)
        {
            if (ExecutionContext.IsFlowSuppressed()) return Task.Run(action);
            using (ExecutionContext.SuppressFlow()) return Task.Run(action);
        }
    }
}
