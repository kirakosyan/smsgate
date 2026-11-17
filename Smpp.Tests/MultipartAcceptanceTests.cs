using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Responses;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public sealed class MultipartAcceptanceTests
    {
        private sealed class Peer : ProbeConnection
        {
            private readonly BlockingCollection<SubmitSmResp> replies = new();
            internal Peer(string command, GateEvents events = null) : base(events, server: command == "submit") { }
            protected override int WriteAcknowledgement(string pdu)
            {
                if (FailAcknowledgement) return 502;
                replies.Add(new SubmitSmResp(pdu));
                return 0;
            }
            internal SubmitSmResp Reply(uint expected)
            {
                Assert.That(replies.TryTake(out var response, 3000), Is.True);
                Assert.That(response.command_status, Is.EqualTo(expected));
                return response;
            }
        }

        // Independent frames exercise both message payload layouts. Use UCS-2 so header
        // octets left in a 16-bit concatenation body's text are immediately observable.
        private static string Segment(string command, string kind, int index, string text, int count = 2, int reference = 0x1234)
        {
            string payload = Convert.ToHexString(System.Text.Encoding.BigEndianUnicode.GetBytes(text));
            string flags = kind == "sar" ? "00" : "40";
            string optional = "";
            if (kind == "sar") optional = $"020C0002{reference:X4}020E0001{count:X2}020F0001{index:X2}";
            else if (kind == "combined") payload = $"097001000804{reference:X4}{count:X2}{index:X2}" + payload;
            else payload = $"060804{reference:X4}{count:X2}{index:X2}" + payload;
            string addresses = "000000310000003200";
            int id = command == "submit" ? 4 : command == "deliver" ? 5 : 0x103;
            string body = command == "data"
                ? addresses + flags + $"00080424{payload.Length / 2:X4}{payload}" + optional
                : addresses + flags + $"0000000000000800{payload.Length / 2:X2}{payload}" + optional;
            return $"{16 + body.Length / 2:X8}{id:X8}0000000000000001{body}";
        }

        [Test]
        public void StandardSegmentsPublishOneOrderedMessage([Values("submit", "deliver", "data")] string command,
            [Values("sar", "udh16", "combined")] string kind)
        {
            var events = new GateEvents();
            var received = new List<string>();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            using var peer = new Peer(command, events);
            peer.MessageHandler(Segment(command, kind, 2, "two"));
            peer.Reply(0);
            Assert.That(received, Is.Empty);
            peer.MessageHandler(Segment(command, kind, 1, "one"));
            peer.Reply(0);
            Assert.That(received, Is.EqualTo(new[] { "onetwo" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConflictsAreRejectedAndAcceptedDuplicatesKeepTheirId(bool controlled)
        {
            using var peer = new Peer("submit");
            int calls = 0;
            if (controlled) peer.InboundMessageHandler = (_, _) =>
                Task.FromResult(MessageAcknowledgement.Accept("id-" + Interlocked.Increment(ref calls)));
            string first = Segment("submit", "sar", 1, "first");
            peer.MessageHandler(first);
            string id = peer.Reply(0).MessageID;
            peer.MessageHandler(Segment("submit", "sar", 0, "zero"));
            peer.Reply(1);
            peer.MessageHandler(Segment("submit", "sar", 1, "different"));
            peer.Reply(1);
            peer.MessageHandler(Segment("submit", "sar", 2, "second", count: 3));
            peer.Reply(1);
            peer.MessageHandler(first);
            Assert.That(peer.Reply(0).MessageID, Is.EqualTo(id));
            peer.MessageHandler(Segment("submit", "sar", 2, "second"));
            peer.Reply(0);
            peer.HoldSession(() => Assert.That(peer.MultipartCount, Is.Zero));
            if (controlled) Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void PendingRejectionReleasesReservationAndFailedWritesReleaseBytes()
        {
            using var peer = new Peer("submit") { MaxMultipartAssemblies = 1 };
            var decision = new TaskCompletionSource<MessageAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.InboundMessageHandler = (_, _) => decision.Task;
            string first = Segment("submit", "sar", 1, "one");
            peer.MessageHandler(first);
            peer.MessageHandler(first);
            peer.Reply(0x58);
            peer.MessageHandler(Segment("submit", "sar", 1, "other", reference: 2));
            peer.Reply(0x58);
            decision.SetResult(MessageAcknowledgement.Reject(8));
            peer.Reply(8);
            peer.InboundMessageHandler = null;
            peer.FailAcknowledgement = true;
            peer.MessageHandler(first);
            peer.FailAcknowledgement = false;
            peer.MessageHandler(Segment("submit", "sar", 1, "start", reference: 3));
            peer.Reply(0);
            Assert.That(peer.MultipartCount, Is.EqualTo(1));
        }

        [Test]
        public void AssemblyAndByteLimitsThrottleBeforeAcknowledgementAndRecoverOnCompletion()
        {
            using var peer = new Peer("submit") { MaxMultipartAssemblies = 1, MaxMultipartBytes = 600 };
            peer.MessageHandler(Segment("submit", "sar", 1, "small"));
            peer.Reply(0);
            peer.MessageHandler(Segment("submit", "sar", 1, "other", reference: 2));
            peer.Reply(0x58);
            peer.MessageHandler(Segment("submit", "sar", 2, new string('a', 100)));
            peer.Reply(0x58);
            peer.MessageHandler(Segment("submit", "sar", 2, "fits"));
            peer.Reply(0);
            peer.MessageHandler(Segment("submit", "sar", 1, "other", reference: 2));
            peer.Reply(0);
            Assert.That(peer.MultipartCount, Is.EqualTo(1));
        }
    }
}
