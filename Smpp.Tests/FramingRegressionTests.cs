using System.Collections.Generic;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class FramingRegressionTests
    {
        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void FullLengthPayloadTlvCanBeReceivedInChunks(string command)
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            string body = new string('a', 65535);
            string frame = Encode(command, body, mode: 2)[0];
            for (int i = 0; i < frame.Length; i += 4096)
                connection.MessageHandler(frame.Substring(i, System.Math.Min(4096, frame.Length - i)));
            Assert.That(messages, Is.EqualTo(new[] { body }));
            connection.Quit();
        }

        [Test]
        public void ThrowingMessageSubscriberDoesNotLoseTheFollowingFrame()
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) =>
            {
                if (body == "bad") throw new System.InvalidOperationException("Application failure");
                messages.Add(body);
            };
            var connection = new ProbeConnection(events);
            connection.MessageHandler(Encode("submit", "bad")[0] + Encode("submit", "good")[0]);
            Assert.That(messages, Is.EqualTo(new[] { "good" }));
            connection.Quit();
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void CompleteFrameFollowedByEveryPossiblePartialFrameIsNeverLost(string command)
        {
            var first = Encode(command, "first")[0];
            var second = Encode(command, "second")[0];
            for (int split = 2; split < second.Length; split += 2)
            {
                var messages = new List<string>();
                var events = new GateEvents();
                events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
                var connection = new ProbeConnection(events);
                connection.MessageHandler(first + second.Substring(0, split));
                Assert.That(messages, Is.EqualTo(new[] { "first" }), "Split at hex character " + split);
                connection.MessageHandler(second.Substring(split));
                Assert.That(messages, Is.EqualTo(new[] { "first", "second" }), "Split at hex character " + split);
                connection.Quit();
            }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void ByteAtATimeAndCoalescedFramesKeepTheirOrder(string command)
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            var first = Encode(command, "one")[0];
            for (int i = 0; i < first.Length; i += 2)
                connection.MessageHandler(first.Substring(i, 2));
            connection.MessageHandler(Encode(command, "two")[0] + Encode(command, "three")[0]);
            Assert.That(messages, Is.EqualTo(new[] { "one", "two", "three" }));
            connection.Quit();
        }

        [TestCase("00000000")]
        [TestCase("0000000F")]
        [TestCase("FFFFFFFF")]
        [TestCase("ZZZZZZZZ")]
        public void BadTrailingLengthDoesNotDiscardPreviousCompleteFrames(string badHeader)
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            connection.MessageHandler(Encode("submit", "first")[0] + badHeader);
            connection.MessageHandler(Encode("submit", "next")[0]);
            Assert.That(messages, Is.EqualTo(new[] { "first", "next" }));
            connection.Quit();
        }

        [Test]
        public void MalformedBodyDoesNotPreventFollowingFrameFromBeingHandled()
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            connection.MessageHandler("00000010000000040000000000000001" + Encode("submit", "valid")[0]);
            Assert.That(messages, Is.EqualTo(new[] { "valid" }));
            connection.Quit();
        }
    }
}
