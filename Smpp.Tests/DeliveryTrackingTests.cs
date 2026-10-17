using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class DeliveryTrackingTests
    {
        internal static string Receipt(string id, byte state, bool data = false, string text = "")
        {
            Pdu message;
            if (data)
                message = new DataSm { Sender = "2", Recipient = "1", Body = text, BodyFormat = "ascii", ESMClass = 4, sequence_number = 100 };
            else
                message = new DeliverSm(new GateEvents()) { Sender = "2", Recipient = "1", Body = text, BodyFormat = "ascii", IsDeliveryReport = true, sequence_number = 100 };
            message.tlv["001E"] = Common.StringToHex(id) + "00";
            message.tlv["0427"] = state.ToString("X2");
            message.tlv["0423"] = "03002A";
            return data ? ((DataSm)message).EncodeM()[0] : ((DeliverSm)message).EncodeM()[0];
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TlvOnlyReceiptsUseStandardFieldsAndExposeNetworkErrors(bool data)
        {
            var receipts = new List<DeliveryReceipt>();
            var messages = new List<string>();
            var events = new GateEvents();
            events.DeliveryReceiptEvent += receipts.Add;
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            try
            {
                connection.MessageHandler(Receipt("provider-42", 2, data));
                Assert.That(receipts, Has.Count.EqualTo(1));
                Assert.That(receipts[0].ProviderMessageId, Is.EqualTo("provider-42"));
                Assert.That(receipts[0].Status, Is.EqualTo(Common.MessageStatus.delivered_ACK_received));
                Assert.That(receipts[0].NetworkErrorCode, Is.EqualTo("03002A"));
                Assert.That(messages, Is.Empty);
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void StandardTlvFieldsOverrideConflictingReceiptText()
        {
            var parsed = new DeliverSm(Receipt("tlv-id", 5, text: "id:text-id stat:DELIVRD err:123"));
            parsed.Decode();
            Assert.That(parsed.Reference, Is.EqualTo("tlv-id"));
            Assert.That(parsed.DeliveryStatus, Is.EqualTo("UNDELI"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EveryPartIdIsExposedAndDeliveryRequiresEveryPart(bool receiptBeforeResponse)
        {
            var parts = new List<MessagePartStatus>();
            var delivery = new List<MessageDeliveryStatus>();
            var receipts = new List<DeliveryReceipt>();
            var events = new GateEvents();
            events.MessagePartStatusEvent += parts.Add;
            events.MessageDeliveryStatusEvent += delivery.Add;
            events.DeliveryReceiptEvent += receipts.Add;
            using var pair = new SocketPair();
            var connection = new ProbeConnection(events) { multypart_delay = 0 };
            try
            {
                connection.Attach(pair.Local);
                connection.SubmitSm(42, "1", "2", new string('a', 161));
                var requests = new[] { ReadPdu(pair.Remote.GetStream()), ReadPdu(pair.Remote.GetStream()) };
                if (receiptBeforeResponse)
                    connection.MessageHandler(Receipt("part-two", 2));
                for (int index = 1; index >= 0; index--)
                {
                    uint sequence = Convert.ToUInt32(requests[index].Substring(24, 8), 16);
                    connection.MessageHandler(new SubmitSmResp { sequence_number = sequence, MessageID = index == 0 ? "part-one" : "part-two" }.Encode());
                }
                Assert.That(parts.Select(part => part.ProviderMessageId), Is.EqualTo(new[] { "part-two", "part-one" }));
                Assert.That(parts.Select(part => part.PartNumber), Is.EqualTo(new[] { 2, 1 }));
                Assert.That(delivery.Any(value => value.IsComplete), Is.False);
                connection.Drop();
                using var replacement = new SocketPair();
                connection.Attach(replacement.Local);
                connection.MessageHandler(Receipt("part-one", 2));
                if (!receiptBeforeResponse)
                {
                    Assert.That(delivery.Any(value => value.IsComplete), Is.False);
                    connection.MessageHandler(Receipt("part-two", 2));
                }
                connection.MessageHandler(Receipt("part-two", 1)); // A late intermediate receipt cannot undo delivery.
                Assert.That(delivery.Count(value => value.IsComplete), Is.EqualTo(1));
                var complete = delivery.Single(value => value.IsComplete);
                Assert.That(complete.MessageId, Is.EqualTo(42));
                Assert.That(complete.Status, Is.EqualTo(Common.MessageStatus.delivered_ACK_received));
                Assert.That(complete.Parts.Select(part => part.ProviderMessageId), Is.EqualTo(new[] { "part-one", "part-two" }));
                Assert.That(receipts.Any(value => value.MessageId == 42 && value.PartNumber == 1 && value.PartCount == 2), Is.True);
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void RejectionsExposeVendorStatusAndCannotBecomeDelivered()
        {
            var parts = new List<MessagePartStatus>();
            var delivery = new List<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessagePartStatusEvent += parts.Add;
            events.MessageDeliveryStatusEvent += delivery.Add;
            using var pair = new SocketPair();
            var connection = new ProbeConnection(events) { multypart_delay = 0 };
            try
            {
                connection.Attach(pair.Local);
                connection.SubmitSm(9, "1", "2", new string('a', 161));
                var first = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                var second = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                connection.MessageHandler(new SubmitSmResp { sequence_number = first.sequence_number, command_status = 0x1234 }.Encode());
                connection.MessageHandler(new SubmitSmResp { sequence_number = second.sequence_number, MessageID = "accepted-part" }.Encode());
                connection.MessageHandler(Receipt("accepted-part", 2));
                Assert.That(parts[0].CommandStatus, Is.EqualTo(0x1234));
                Assert.That(parts, Has.Count.EqualTo(2));
                Assert.That(delivery.Count(value => value.IsComplete), Is.EqualTo(1));
                Assert.That(delivery.Single(value => value.IsComplete).Status, Is.EqualTo(Common.MessageStatus.rejected));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void TrackingCapacityExpiresUnfinishedDeliveryRatherThanGrowingWithoutLimit()
        {
            var results = new List<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessageDeliveryStatusEvent += results.Add;
            using var pair = new SocketPair();
            var connection = new ProbeConnection(events) { MaxTrackedDeliveryParts = 1 };
            try
            {
                connection.Attach(pair.Local);
                for (int id = 1; id <= 2; id++)
                {
                    connection.SubmitSm(id, "1", "2", "hello");
                    var request = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                    connection.MessageHandler(new SubmitSmResp { sequence_number = request.sequence_number, MessageID = "provider-" + id }.Encode());
                }
                Assert.That(results.Single(value => value.MessageId == 1).Status, Is.EqualTo(Common.MessageStatus.timeout));
                connection.MessageHandler(Receipt("provider-2", 2));
                Assert.That(results.Single(value => value.MessageId == 2 && value.IsComplete).Status, Is.EqualTo(Common.MessageStatus.delivered_ACK_received));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void MissingSubmitResponseEmitsPartTimeoutWithoutInventingAProviderError()
        {
            var parts = new List<MessagePartStatus>();
            var events = new GateEvents();
            events.MessagePartStatusEvent += parts.Add;
            using var pair = new SocketPair();
            var connection = new ProbeConnection(events);
            try
            {
                connection.Attach(pair.Local);
                connection.SubmitSm(42, "1", "2", "hello");
                var request = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                connection.AgeSubmission(request.sequence_number);
                connection.Tick();
                Assert.That(parts.Single().CommandStatus, Is.Null);
                Assert.That(parts.Single().Status, Is.EqualTo(Common.MessageStatus.timeout));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void ReusedProviderIdsCannotFalselyDeliverTwoDifferentMessages()
        {
            var results = new List<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessageDeliveryStatusEvent += results.Add;
            using var pair = new SocketPair();
            var connection = new ProbeConnection(events);
            try
            {
                connection.Attach(pair.Local);
                for (int id = 1; id <= 2; id++)
                {
                    connection.SubmitSm(id, "1", "2", "hello");
                    var request = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                    connection.MessageHandler(new SubmitSmResp { sequence_number = request.sequence_number, MessageID = "reused-id" }.Encode());
                }
                connection.MessageHandler(Receipt("reused-id", 2));
                Assert.That(results.Count(value => value.IsComplete), Is.EqualTo(2));
                Assert.That(results.Select(value => value.Status), Is.All.EqualTo(Common.MessageStatus.generic_error));
            }
            finally { connection.Quit(); }
        }
    }
}
