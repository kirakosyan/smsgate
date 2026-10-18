using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;

namespace Smpp.Tests
{
    internal static class ReviewTestSupport
    {
        internal static string[] Encode(string command, string body, string format = "ascii", int mode = 1,
            string sender = "1", string recipient = "2", byte reference = 42, bool packed = false)
        {
            if (command == "submit")
                return new SubmitSm
                {
                    Sender = sender, Recipient = recipient, Body = body, BodyFormat = format,
                    RegisteredDelivery = 0, ServiceType = "", LargeMessageHandleMethod = mode,
                    SplitBaseASCII = 153, sequence_number = 42, MultipartSequenceNumber = reference, Is8bit = packed
                }.EncodeM();
            if (command == "deliver")
                return new DeliverSm(new GateEvents())
                {
                    Sender = sender, Recipient = recipient, Body = body, BodyFormat = format,
                    RegisteredDelivery = 0, LargeMessageHandleMethod = mode, SplitBaseASCII = 153,
                    sequence_number = 42, MultipartSequenceNumber = reference, Is8bit = packed
                }.EncodeM();
            return new DataSm
            {
                Sender = sender, Recipient = recipient, Body = body, BodyFormat = format,
                RegisteredDelivery = 0, sequence_number = 42
            }.EncodeM();
        }

        internal static (string Body, string Format, Common.MultipartMessage? Multipart) Decode(string command, string hex, bool packed = false)
        {
            if (command == "submit")
            {
                var message = new SubmitSm(hex) { Is8bit = packed };
                if (packed) message.Decode();
                return (message.Body, message.BodyFormat, message.isMultipart ? message.MultipartMessage : null);
            }
            if (command == "deliver")
            {
                var message = new DeliverSm(hex) { Is8bit = packed };
                message.Decode();
                return (message.Body, message.BodyFormat, message.isMultipart ? message.MultipartMessage : null);
            }
            var data = new DataSm(hex);
            return (data.Body, data.BodyFormat, null);
        }

        internal static string Body(string format, int length)
        {
            if (format == "binary")
                return Convert.ToHexString(Enumerable.Range(0, length).Select(i => (byte)(i % 256)).ToArray());
            string alphabet = format == "unicode" ? "АБВГДЕЖ" : format == "latin" ? "æøåÆØÅé" : "abcdefg";
            return new string(Enumerable.Range(0, length).Select(i => alphabet[i % alphabet.Length]).ToArray());
        }

        internal static void WaitUntil(Func<bool> condition, string description, int milliseconds = 4000)
        {
            Assert.That(SpinWait.SpinUntil(condition, milliseconds), Is.True, description);
        }

        internal static string ReadPdu(NetworkStream stream)
        {
            stream.ReadTimeout = 4000;
            var header = new byte[4];
            stream.ReadExactly(header);
            int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
            Assert.That(length, Is.InRange(16, 100000));
            var bytes = new byte[length];
            header.CopyTo(bytes, 0);
            stream.ReadExactly(bytes.AsSpan(4));
            return Convert.ToHexString(bytes);
        }

        internal static void WritePdu(NetworkStream stream, string hex) => stream.Write(Convert.FromHexString(hex));

        internal class ProbeConnection : Connection
        {
            internal int Reconnects;
            internal ProbeConnection(GateEvents events = null) : base("probe", events ?? new GateEvents()) { }
            protected override void ScheduleReconnect(int delayMilliseconds) => Interlocked.Increment(ref Reconnects);
            internal bool FailAcknowledgement;
            protected override int WriteAcknowledgement(string pdu) => FailAcknowledgement ? 502 : tcp_stream == null ? 0 : base.WriteAcknowledgement(pdu);
            internal int Pending { get { lock (SessionLock) return submitted_messages.Count; } }
            internal int MultipartCount { get { lock (SessionLock) return multipart_messages.Count; } }
            internal bool TimerEnabled => timeout_timer.Enabled;
            internal void SetNextSequence(uint sequence) { lock (SessionLock) sequence_number = sequence; }
            internal void Attach(TcpClient client) { lock (SessionLock) BeginSession(client, true); }
            internal void Drop() { Disconnect(true); Drain(); }
            internal void Drain() => DrainNotificationsAsync().WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
            // Deterministically observe asynchronous notifications in parser-only tests.
            public new void MessageHandler(object message) { base.MessageHandler(message); Drain(); }
            public new void SubmitDeliveryReport(string id, Common.MessageStatus status) { base.SubmitDeliveryReport(id, status); Drain(); }
            public new void Quit() { base.Quit(); Drain(); }
            internal void Track(uint sequence, int id, DateTime submitted)
            {
                TryTrackSubmittedMessages(sequence, 1, new submitted_message { message_id = id, submitted_time = submitted });
            }
            internal void TrackParts(uint sequence, int count, int id, DateTime submitted) =>
                TryTrackSubmittedMessages(sequence, count, new submitted_message { message_id = id, submitted_time = submitted });
            internal int SendDeliver(int id) => SendMessage(id, "1", "2", true, "deliver_sm", (sequence, reference) =>
                new DeliverSm(new GateEvents()) { Sender = "1", Recipient = "2", Body = "hello", BodyFormat = "ascii", sequence_number = sequence }.EncodeM());
            internal void AgeSubmission(uint sequence)
            {
                lock (SessionLock)
                {
                    var message = submitted_messages[sequence];
                    message.submitted_time = DateTime.UtcNow.AddMinutes(-10);
                    submitted_messages[sequence] = message;
                }
            }
            internal void HoldSession(Action action) { lock (SessionLock) action(); }
            internal bool SessionLockHeld => Monitor.IsEntered(SessionLock);
            internal void Queue(uint sequence, DateTime submitted)
            {
                lock (SessionLock) commands_queue.Add(sequence, submitted);
            }
            internal void Tick()
            {
                typeof(Connection).GetMethod("timeout_timer_Elapsed", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(this, new object[] { null, null });
                Drain();
            }
            internal void AgeMultipart()
            {
                for (int i = 0; i < multipart_messages.Count; i++)
                {
                    var part = multipart_messages[i];
                    part.submitted_dt = DateTime.UtcNow.AddMinutes(-10);
                    multipart_messages[i] = part;
                }
            }
        }

        internal sealed class SocketPair : IDisposable
        {
            internal TcpClient Local { get; }
            internal TcpClient Remote { get; }
            internal SocketPair()
            {
                using var listener = new TestTcpListener(IPAddress.Loopback, 0);
                listener.Start();
                Local = new TcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
                Remote = listener.AcceptTcpClient();
            }
            public void Dispose() { Local.Dispose(); Remote.Dispose(); }
        }
    }
}
