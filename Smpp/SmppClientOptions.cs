using System;
using System.Net.Security;
using System.Linq;
using Smpp.Events;

namespace Smpp
{
    /// <summary>Validated startup configuration. Credentials are excluded from its string representation.</summary>
    public sealed class SmppClientOptions
    {
        public string ChannelName { get; init; } = "smpp";
        public string Host { get; init; }
        public int Port { get; init; } = 2775;
        public string SystemId { get; init; }
        public string Password { get; init; }
        public bool Receive { get; init; } = true;
        public bool Transmit { get; init; } = true;
        public bool UseGsm7 { get; init; } = true;
        public int SourceTon { get; init; }
        public int SourceNpi { get; init; }
        public int DestinationTon { get; init; } = 1;
        public int DestinationNpi { get; init; } = 1;
        public int ResponseTimeoutMilliseconds { get; init; } = 10000;
        public int ReconnectDelayMilliseconds { get; init; } = 10000;
        public SslClientAuthenticationOptions TlsOptions { get; init; }
        public SubmissionOptions Submission { get; init; } = new();
        public DeliveryTrackingStore DeliveryStore { get; init; }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ChannelName) || string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("A channel name and host are required.");
            if (Port < 1 || Port > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
            if (!Receive && !Transmit) throw new ArgumentException("Enable at least one bind direction.");
            ValidateAscii(SystemId, 15, nameof(SystemId));
            ValidateAscii(Password, 8, nameof(Password));
            foreach (int value in new[] { SourceTon, SourceNpi, DestinationTon, DestinationNpi })
                if (value < 0 || value > 255) throw new ArgumentException("TON and NPI must fit one octet.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ResponseTimeoutMilliseconds);
            ArgumentOutOfRangeException.ThrowIfNegative(ReconnectDelayMilliseconds);
            if (TlsOptions != null && string.IsNullOrWhiteSpace(TlsOptions.TargetHost)) throw new ArgumentException("TLS requires a certificate TargetHost.");
            ArgumentNullException.ThrowIfNull(Submission);
            Submission.Validate();
        }
        private static void ValidateAscii(string text, int maximum, string name)
        {
            if (text == null || text.Length > maximum || text.Any(value => value == 0 || value > 127))
                throw new ArgumentException("Invalid ASCII credential length or contents.", name);
        }
        public Client CreateClient(GateEvents events)
        {
            Validate();
            var client = new Client(ChannelName, events, registerWithGate: false)
            {
                host = Host,
                port = Port,
                system_id = SystemId,
                password = Password,
                direction_in = Receive,
                direction_out = Transmit,
                UseGsm7 = UseGsm7,
                source_ton = SourceTon,
                source_npi = SourceNpi,
                destination_ton = DestinationTon,
                destination_npi = DestinationNpi,
                timeout = ResponseTimeoutMilliseconds,
                reconnectDelay = ReconnectDelayMilliseconds,
                TlsOptions = TlsOptions,
                SubmissionOptions = Submission
            };
            if (DeliveryStore != null) client.DeliveryTrackingStore = DeliveryStore;
            return client;
        }
        public override string ToString() => $"{ChannelName} ({Host}:{Port})";
    }
}
