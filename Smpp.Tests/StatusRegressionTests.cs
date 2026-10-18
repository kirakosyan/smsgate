using System;
using System.Collections.Generic;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class StatusRegressionTests
    {
        [Test]
        public void OutgoingReceiptWithLongMessageIdRetainsItsEntireBody()
        {
            string encoded = null;
            var events = new GateEvents();
            events.ChannelEvent += (_, description, pdu) => { if (description == "Sending [deliver_sm] report") encoded = pdu; };
            var connection = new ProbeConnection(events) { debug = true };
            string id = new string('a', 65);
            connection.SubmitDeliveryReport(id, Common.MessageStatus.rejected);
            var report = new DeliverSm(encoded);
            report.Decode();
            Assert.That(report.isMultipart, Is.False);
            Assert.That(report.Reference, Is.EqualTo(id));
            Assert.That(report.DeliveryStatus, Is.EqualTo("REJECT"));
            connection.Quit();
        }

        [TestCase(0x80000004u)]
        [TestCase(0x80000005u)]
        [TestCase(0x80000103u)]
        public void EveryNonzeroResponseIsRejectedAndEachResponseCompletesOnlyOnce(uint command)
        {
            var statuses = new List<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Add(status);
            var connection = new ProbeConnection(events);
            foreach (uint error in new uint[] { 0, 13, 0x123, 0xFFFFFFFF })
            {
                connection.Track(42, 1, DateTime.UtcNow);
                string response = $"00000010{command:X8}{error:X8}0000002A";
                connection.MessageHandler(response);
                connection.MessageHandler(response);
                Assert.That(statuses[statuses.Count - 1], Is.EqualTo(error == 0 ? Common.MessageStatus.sent : Common.MessageStatus.rejected));
                Assert.That(connection.Pending, Is.Zero);
            }
            Assert.That(statuses, Has.Count.EqualTo(4));
            connection.Quit();
        }

        [Test]
        public void GenericNackRejectsPendingMessageInsteadOfLeavingItForTimeout()
        {
            var statuses = new List<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Add(status);
            var connection = new ProbeConnection(events);
            connection.Track(42, 1, DateTime.UtcNow);
            connection.MessageHandler("0000001080000000000000030000002A");
            Assert.That(statuses, Is.EqualTo(new[] { Common.MessageStatus.rejected }));
            Assert.That(connection.Pending, Is.Zero);
            connection.Quit();
        }

        [TestCase("id:42")]
        [TestCase("id:42 stat:")]
        [TestCase("id:42 stat:D")]
        [TestCase("id:42 stat:DELIV")]
        [TestCase("id:42 stat:DELIVRDX")]
        [TestCase("id:42 stat:SOMETHING")]
        [TestCase("stat:DELIVRD")]
        [TestCase("id: stat:DELIVRD")]
        [TestCase("id: sub:001 stat:DELIVRD")]
        [TestCase("")]
        public void MissingUnknownAndTruncatedReceiptFieldsNeverReportSuccess(string body)
        {
            var statuses = new List<Common.MessageStatus>();
            var errors = new List<string>();
            var events = new GateEvents();
            events.MessageDeliveryReportEvent += (_, status) => statuses.Add(status);
            events.Event += (_, description) => errors.Add(description);
            var connection = new ProbeConnection(events);
            var report = new DeliverSm(events) { Sender = "1", Recipient = "2", Body = body, BodyFormat = "ascii", IsDeliveryReport = true };
            Assert.DoesNotThrow(() => connection.MessageHandler(report.EncodeM()[0]));
            Assert.That(statuses, Is.Empty);
            Assert.That(errors, Has.Count.EqualTo(1));
            connection.Quit();
        }

        [TestCase("DELIVRD", Common.MessageStatus.delivered_ACK_received)]
        [TestCase("delivr", Common.MessageStatus.delivered_ACK_received)]
        [TestCase("EXPIRED", Common.MessageStatus.ACK_expired)]
        [TestCase("UNDELIV", Common.MessageStatus.message_undeliverable)]
        [TestCase("REJECTD", Common.MessageStatus.rejected)]
        [TestCase("ACCEPTD", Common.MessageStatus.received_routed)]
        [TestCase("UNKNOWN", Common.MessageStatus.unknown_recipient)]
        [TestCase("ENROUTE", Common.MessageStatus.submitted_waiting_for_ACK)]
        [TestCase("DELETED", Common.MessageStatus.message_undeliverable)]
        public void KnownReceiptsMapToTheirActualStatus(string status, Common.MessageStatus expected)
        {
            var statuses = new List<(string, Common.MessageStatus)>();
            var events = new GateEvents();
            events.MessageDeliveryReportEvent += (id, value) => statuses.Add((id, value));
            var connection = new ProbeConnection(events);
            var report = new DeliverSm(events)
            {
                Sender = "1", Recipient = "2", Body = "id:msg_42 stat: " + status + " err:0", BodyFormat = "unicode",
                IsDeliveryReport = true, ESMClass = 0x84
            };
            connection.MessageHandler(report.EncodeM()[0]);
            Assert.That(statuses, Is.EqualTo(new[] { ("msg_42", expected) }));
            connection.Quit();
        }

        [Test]
        public void ReceiptIdAndStatusCanArriveInDifferentMultipartSegments()
        {
            var statuses = new List<(string, Common.MessageStatus)>();
            var events = new GateEvents();
            events.MessageDeliveryReportEvent += (id, status) => statuses.Add((id, status));
            var connection = new ProbeConnection(events);
            var report = new DeliverSm(events)
            {
                Sender = "1", Recipient = "2", BodyFormat = "ascii", IsDeliveryReport = true,
                Body = "id:42 text:" + new string('a', 200) + " stat:DELIVRD"
            };
            var parts = report.EncodeM();
            connection.MessageHandler(parts[1]);
            Assert.That(statuses, Is.Empty);
            connection.MessageHandler(parts[0]);
            Assert.That(statuses, Is.EqualTo(new[] { ("42", Common.MessageStatus.delivered_ACK_received) }));
            connection.Quit();
        }

        [TestCase(Common.MessageStatus.ACK_expired, "EXPIRE")]
        [TestCase(Common.MessageStatus.timeout, "EXPIRE")]
        [TestCase(Common.MessageStatus.rejected, "REJECT")]
        [TestCase(Common.MessageStatus.sent, "ENROUT")]
        [TestCase(Common.MessageStatus.delivered_ACK_received, "DELIVR")]
        [TestCase((Common.MessageStatus)999, "UNKNOW")]
        public void OutgoingReportsDoNotPromotePendingOrFailureStatesToDelivered(Common.MessageStatus status, string expected)
        {
            string encoded = null;
            var events = new GateEvents();
            events.ChannelEvent += (_, description, pdu) => { if (description == "Sending [deliver_sm] report") encoded = pdu; };
            var connection = new ProbeConnection(events) { debug = true };
            connection.SubmitDeliveryReport("42", status);
            var report = new DeliverSm(encoded);
            report.Decode();
            Assert.That(report.DeliveryStatus, Is.EqualTo(expected));
            connection.Quit();
        }
    }
}
