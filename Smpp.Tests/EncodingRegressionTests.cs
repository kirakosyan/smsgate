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
    public class EncodingRegressionTests
    {
        public static IEnumerable<TestCaseData> MessageCases()
        {
            foreach (string command in new[] { "submit", "deliver", "data" })
            foreach (string format in new[] { "ascii", "latin", "unicode", "binary" })
            foreach (int length in new[] { 0, 1, 7, 8, 69, 70, 71, 139, 140, 141, 159, 160, 161, 267, 268, 269, 306, 307, 308, 1378, 1380, 1530 })
            foreach (int mode in command == "data" ? new[] { 2 } : new[] { 1, 2, 3 })
                yield return new TestCaseData(command, format, length, mode);
        }

        [TestCaseSource(nameof(MessageCases))]
        public void MessageBodiesRoundTripAcrossFormatsModesAndBoundaries(string command, string format, int length, int mode)
        {
            string body = Body(format, length);
            string[] frames = Encode(command, body, format, mode);
            var decoded = frames.Select(frame => Decode(command, frame)).ToArray();
            int singleLimit = format == "unicode" ? 70 : format == "ascii" ? 160 : 140;
            string expected = mode == 3 && length > singleLimit
                ? body.Substring(0, singleLimit * (format == "binary" ? 2 : 1)) : body;
            Assert.That(string.Concat(decoded.Select(message => message.Body)), Is.EqualTo(expected));
            Assert.That(decoded.Select(message => message.Format), Is.All.EqualTo(format));
            for (int i = 0; i < frames.Length; i++)
            {
                byte[] wire = Convert.FromHexString(frames[i]);
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(wire), Is.EqualTo(wire.Length));
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(12)), Is.EqualTo(42 + i));
                if (command != "data" && mode != 2)
                    Assert.That(wire[34], Is.EqualTo(wire.Length - 35), "sm_length must count octets");
                if (frames.Length > 1)
                {
                    Assert.That(wire[35..38], Is.EqualTo(new byte[] { 5, 0, 3 }));
                    Assert.That(wire[38], Is.EqualTo(42));
                    Assert.That(wire[39], Is.EqualTo(frames.Length), "UDH count must be hexadecimal on the wire");
                    Assert.That(wire[40], Is.EqualTo(i + 1));
                    Assert.That(decoded[i].Multipart.Value.num_of_parts, Is.EqualTo(frames.Length));
                }
            }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void AllTwoAndThreeSegmentBoundariesPreserveEveryCharacter(string command)
        {
            foreach (string format in new[] { "ascii", "unicode", "latin", "binary" })
            for (int length = 0; length <= 470; length++)
            {
                string body = Body(format, length);
                var frames = Encode(command, body, format);
                Assert.That(string.Concat(frames.Select(frame => Decode(command, frame).Body)), Is.EqualTo(body),
                    $"{format}, length {length}");
            }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void UnicodeSurrogatePairsAreNotSplitOrTruncatedInHalf(string command)
        {
            string body = new string('a', 62) + "😀" + new string('b', 62) + "😀";
            var frames = Encode(command, body, "unicode");
            Assert.That(string.Concat(frames.Select(frame => Decode(command, frame).Body)), Is.EqualTo(body));
            string truncated = new string('a', 69) + "😀";
            Assert.That(Decode(command, Encode(command, truncated, "unicode", 3)[0]).Body, Is.EqualTo(new string('a', 69)));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void PackedAsciiRoundTripsAcrossEveryPaddingAndMultipartBoundary(string command)
        {
            for (int length = 0; length <= 320; length++)
            {
                string body = Body("ascii", length);
                var frames = Encode(command, body, packed: true);
                Assert.That(string.Concat(frames.Select(frame => Decode(command, frame, true).Body)), Is.EqualTo(body),
                    "Length " + length);
            }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void MultipartCountSupports255ButRejects256(string command)
        {
            var frames = Encode(command, new string('a', 255 * 153));
            Assert.That(frames, Has.Length.EqualTo(255));
            Assert.That(Convert.FromHexString(frames[^1])[39..41], Is.EqualTo(new byte[] { 255, 255 }));
            Assert.Throws<ArgumentException>(() => Encode(command, new string('a', 255 * 153 + 1)));
        }

        [TestCase("submit", "ascii", 161)]
        [TestCase("deliver", "unicode", 71)]
        [TestCase("submit", "latin", 141)]
        [TestCase("deliver", "binary", 141)]
        public void FailModeRejectsOversizeMessages(string command, string format, int length)
        {
            Assert.Throws<ArgumentException>(() => Encode(command, Body(format, length), format, 4));
            Assert.DoesNotThrow(() => Encode(command, Body(format, length - 1), format, 4));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void BinaryBodiesRequireValidWholeHexOctets(string command)
        {
            Assert.Throws<FormatException>(() => Encode(command, "ABC", "binary"));
            Assert.Throws<FormatException>(() => Encode(command, "GG", "binary"));
            Assert.That(Decode(command, Encode(command, "00ff80", "binary")[0]).Body, Is.EqualTo("00FF80"));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void PayloadTlvAcceptsMaximumLengthButRejectsOverflow(string command)
        {
            string body = new string('a', 65535);
            Assert.That(Decode(command, Encode(command, body, mode: 2)[0]).Body, Is.EqualTo(body));
            Assert.Throws<ArgumentException>(() => Encode(command, body + "a", mode: 2));
        }

        [TestCase("submit", 1)]
        [TestCase("deliver", 1)]
        [TestCase("submit", 2)]
        [TestCase("deliver", 2)]
        [TestCase("data", 2)]
        public void ManualPayloadCannotDuplicateAnEncodedBody(string command, int mode)
        {
            Assert.Throws<ArgumentException>(() => EncodeWithManualPayload(command, "inline", mode));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void ManualPayloadWithAnEmptyShortMessageRemainsValid(string command)
        {
            string frame = EncodeWithManualPayload(command, "", 1);
            Assert.That(Convert.FromHexString(frame)[34], Is.Zero);
            Assert.That(Decode(command, frame).Body, Is.EqualTo("payload"));
        }

        private static string EncodeWithManualPayload(string command, string body, int mode)
        {
            if (command == "submit")
            {
                var request = new SubmitSm { Sender = "1", Recipient = "2", Body = body, LargeMessageHandleMethod = mode };
                request.tlv["0424"] = Common.StringToHex("payload");
                return request.EncodeM()[0];
            }
            if (command == "deliver")
            {
                var request = new DeliverSm(new GateEvents()) { Sender = "1", Recipient = "2", Body = body, LargeMessageHandleMethod = mode };
                request.tlv["0424"] = Common.StringToHex("payload");
                return request.EncodeM()[0];
            }
            var data = new DataSm { Sender = "1", Recipient = "2", Body = body };
            data.tlv["0424"] = Common.StringToHex("payload");
            return data.EncodeM()[0];
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void WapPushEncodingRemainsSupportedAndRepeatable(string command)
        {
            string frame = Encode(command, "https://www.example.com", "wap_push", 2)[0];
            var decoded = Decode(command, frame);
            Assert.That(decoded.Body, Does.StartWith("0605040B8423F0"));
            Assert.That(decoded.Format, Is.EqualTo("wap_push"));
            Assert.That(Decode(command, Encode(command, decoded.Body, "wap_push", 2)[0]).Body, Is.EqualTo(decoded.Body));
        }

        public static IEnumerable<Pdu> Responses()
        {
            yield return new BindReceiverResp();
            yield return new BindTransmitterResp();
            yield return new BindTransceiverResp();
            yield return new SubmitSmResp { MessageID = "id" };
            yield return new DeliverSmResp();
            yield return new DataSmResp { MessageID = "id" };
            yield return new GenericNack();
            yield return new EnquireLinkResp();
            yield return new UnbindResp();
        }

        [TestCaseSource(nameof(Responses))]
        public void EveryResponsePreservesSuccessAndErrorStatus(Pdu response)
        {
            foreach (uint status in new uint[] { 0, 5, 13, 0x123, 0xFFFFFFFF })
            {
                response.command_status = status;
                response.sequence_number = 123;
                byte[] wire = Convert.FromHexString(response.Encode());
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(8)), Is.EqualTo(status));
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(12)), Is.EqualTo(123));
            }
        }
    }
}
