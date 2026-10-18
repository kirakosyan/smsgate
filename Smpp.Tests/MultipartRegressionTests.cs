using System;
using System.Collections.Generic;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class MultipartRegressionTests
    {
        [TestCase("submit")]
        [TestCase("deliver")]
        public void PackedMultipartMessagesAreDecodedByTheConnection(string command)
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events) { use8bit = true };
            string body = Body("ascii", 307);
            foreach (var part in Encode(command, body, packed: true))
                connection.MessageHandler(part);
            Assert.That(messages, Is.EqualTo(new[] { body }));
            connection.Quit();
        }

        [TestCase("submit", false)]
        [TestCase("submit", true)]
        [TestCase("deliver", false)]
        [TestCase("deliver", true)]
        public void SharedReferencesDoNotMixSendersOrRecipients(string command, bool differentSender)
        {
            var received = new List<(string Sender, string Recipient, string Body)>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, sender, recipient, body, _, _) => received.Add((sender, recipient, body));
            var connection = new ProbeConnection(events);
            string first = new string('a', 170), second = new string('b', 170);
            var a = Encode(command, first, sender: "1", recipient: "2");
            var b = Encode(command, second, sender: differentSender ? "3" : "1", recipient: differentSender ? "2" : "4");
            connection.MessageHandler(a[0]);
            connection.MessageHandler(b[1]);
            Assert.That(received, Is.Empty, "Partial messages from different addresses must remain separate");
            connection.MessageHandler(a[1]);
            connection.MessageHandler(b[0]);
            Assert.That(received, Is.EqualTo(new[] { ("1", "2", first), (differentSender ? "3" : "1", differentSender ? "2" : "4", second) }));
            Assert.That(connection.MultipartCount, Is.Zero);
            connection.Quit();
        }

        [TestCase("submit", "unicode")]
        [TestCase("deliver", "latin")]
        [TestCase("submit", "binary")]
        public void SharedReferencesDoNotMixFormats(string command, string otherFormat)
        {
            var received = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            var connection = new ProbeConnection(events);
            var a = Encode(command, Body("ascii", 170));
            int length = otherFormat == "unicode" ? 80 : 170;
            var b = Encode(command, Body(otherFormat, length), otherFormat);
            connection.MessageHandler(a[0]);
            connection.MessageHandler(b[1]);
            Assert.That(received, Is.Empty);
            connection.MessageHandler(a[1]);
            connection.MessageHandler(b[0]);
            Assert.That(received, Is.EqualTo(new[] { Body("ascii", 170), Body(otherFormat, length) }));
            connection.Quit();
        }

        [Test]
        public void SubmitAndDeliverAssembliesWithSameReferenceRemainSeparate()
        {
            var received = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            var connection = new ProbeConnection(events);
            var a = Encode("submit", new string('a', 170));
            var b = Encode("deliver", new string('b', 170));
            connection.MessageHandler(a[0]);
            connection.MessageHandler(b[1]);
            Assert.That(received, Is.Empty);
            connection.MessageHandler(a[1]);
            connection.MessageHandler(b[0]);
            Assert.That(received, Is.EqualTo(new[] { new string('a', 170), new string('b', 170) }));
            connection.Quit();
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void DuplicatedAndOutOfOrderSegmentsProduceExactlyOneCompleteMessage(string command)
        {
            var received = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            var connection = new ProbeConnection(events);
            string body = Body("ascii", 307);
            var parts = Encode(command, body);
            connection.MessageHandler(parts[2]);
            connection.MessageHandler(parts[0]);
            connection.MessageHandler(parts[0]);
            connection.MessageHandler(parts[2]);
            Assert.That(received, Is.Empty);
            Assert.That(connection.MultipartCount, Is.EqualTo(2));
            connection.MessageHandler(parts[1]);
            Assert.That(received, Is.EqualTo(new[] { body }));
            Assert.That(connection.MultipartCount, Is.Zero);
            connection.Quit();
        }

        [TestCase("submit", 0, 1)]
        [TestCase("submit", 2, 0)]
        [TestCase("submit", 2, 3)]
        [TestCase("deliver", 0, 1)]
        [TestCase("deliver", 2, 0)]
        [TestCase("deliver", 2, 3)]
        public void InvalidSegmentNumbersDoNotPoisonAssembly(string command, int count, int index)
        {
            var parts = Encode(command, new string('a', 170));
            byte[] invalid = Convert.FromHexString(parts[0]);
            invalid[39] = (byte)count;
            invalid[40] = (byte)index;
            var connection = new ProbeConnection();
            connection.MessageHandler(Convert.ToHexString(invalid));
            Assert.That(connection.MultipartCount, Is.Zero);
            connection.MessageHandler(parts[0]);
            connection.MessageHandler(parts[1]);
            Assert.That(connection.MultipartCount, Is.Zero);
            connection.Quit();
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void ConflictingDuplicatesAndCountsDoNotOverwriteOriginalSegments(string command)
        {
            var received = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            var connection = new ProbeConnection(events);
            string body = new string('a', 170);
            var parts = Encode(command, body);
            connection.MessageHandler(parts[0]);
            var conflict = Convert.FromHexString(parts[0]);
            conflict[conflict.Length - 1] = (byte)'b';
            connection.MessageHandler(Convert.ToHexString(conflict));
            var countConflict = Convert.FromHexString(parts[1]);
            countConflict[39] = 3;
            connection.MessageHandler(Convert.ToHexString(countConflict));
            connection.MessageHandler(parts[1]);
            Assert.That(received, Is.EqualTo(new[] { body }));
            connection.Quit();
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void ExpiredFragmentsAreDiscardedBeforeReferenceReuse(string command)
        {
            var received = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Add(body);
            var connection = new ProbeConnection(events);
            var old = Encode(command, new string('a', 170));
            var current = Encode(command, new string('b', 170));
            connection.MessageHandler(old[0]);
            connection.AgeMultipart();
            connection.MessageHandler(current[1]);
            Assert.That(received, Is.Empty);
            connection.MessageHandler(current[0]);
            Assert.That(received, Is.EqualTo(new[] { new string('b', 170) }));
            connection.Quit();
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void HeaderLikeBinaryContentWithoutUdhiIsNotReassembled(string command)
        {
            const string body = "0500032A0201FF00";
            var decoded = Decode(command, Encode(command, body, "binary")[0]);
            Assert.That(decoded.Body, Is.EqualTo(body));
            Assert.That(decoded.Multipart, Is.Null);
        }
    }
}
