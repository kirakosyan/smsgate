using System;
using System.Linq;
using NUnit.Framework;
using Smpp.Requests;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class Gsm7Tests
    {
        [Test]
        public void KnownAlphabetAndExtensionVectorsMatchTheWireSpecification()
        {
            Assert.That(Convert.ToHexString(Gsm7Encoding.Encode("@£$¥èéùìòÇ")), Is.EqualTo("00010203040506070809"));
            Assert.That(Convert.ToHexString(Gsm7Encoding.Encode("\f^{}\\[~]|€")),
                Is.EqualTo("1B0A1B141B281B291B2F1B3C1B3D1B3E1B401B65"));
            string alphabet = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà\f^{}\\[~]|€";
            Assert.That(Gsm7Encoding.Decode(Gsm7Encoding.Encode(alphabet)), Is.EqualTo(alphabet));
            Assert.That(Gsm7Encoding.GetSeptetCount("hello €"), Is.EqualTo(8));
        }

        [TestCase("😀")]
        [TestCase("Привет")]
        [TestCase("\u001b")]
        public void UnsupportedTextIsRejectedRatherThanReplaced(string body)
        {
            Assert.That(Gsm7Encoding.CanEncode(body), Is.False);
            Assert.Throws<ArgumentException>(() => Gsm7Encoding.Encode(body));
        }

        [TestCase("1B")]
        [TestCase("1B00")]
        [TestCase("FF")]
        public void MalformedSeptetsAreRejected(string hex) =>
            Assert.Throws<FormatException>(() => Gsm7Encoding.Decode(Convert.FromHexString(hex), strict: true));

        [TestCase("1B", " ")]
        [TestCase("1B1B", " ")]
        [TestCase("1B00", "@")]
        public void UnsupportedExtensionsHaveReceiveFallbacks(string hex, string expected) =>
            Assert.That(Gsm7Encoding.Decode(Convert.FromHexString(hex)), Is.EqualTo(expected));
        [TestCase("submit")]
        [TestCase("deliver")]
        public void PackedTrailingCarriageReturnsArePreserved(string command)
        {
            for (int length = 1; length <= 320; length++)
            {
                string text = new string('a', length - 1) + "\r";
                var frames = Encode(command, text, "gsm7", packed: true);
                Assert.That(string.Concat(frames.Select(frame => DecodeGsm(command, frame, true))), Is.EqualTo(text), "length=" + length);
            }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void GsmAlphabetAndExtensionsRoundTripOnEveryMessageCommand(string command)
        {
            string body = "@£Δ€{}[]^~|\\æøå@";
            string frame = Encode(command, body, "gsm7")[0];
            Assert.That(DecodeGsm(command, frame), Is.EqualTo(body));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void SegmentAndTruncationLimitsCountEscapesWithoutSplittingThem(string command)
        {
            Assert.That(Encode(command, new string('€', 80), "gsm7"), Has.Length.EqualTo(1));
            var frames = Encode(command, new string('€', 81), "gsm7");
            Assert.That(frames, Has.Length.EqualTo(2));
            Assert.That(DecodeGsm(command, frames[0]), Has.Length.EqualTo(76));
            Assert.That(string.Concat(frames.Select(frame => DecodeGsm(command, frame))), Is.EqualTo(new string('€', 81)));
            Assert.That(DecodeGsm(command, Encode(command, new string('€', 81), "gsm7", 3)[0]), Is.EqualTo(new string('€', 80)));
            Assert.Throws<ArgumentException>(() => Encode(command, new string('€', 81), "gsm7", 4));
            string splitEscape = new string('a', 152) + "€" + new string('b', 10);
            Assert.That(string.Concat(Encode(command, splitEscape, "gsm7").Select(frame => DecodeGsm(command, frame))), Is.EqualTo(splitEscape));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        public void PackedGsmUsesSeptetMappingAndPreservesTrailingAtSigns(string command)
        {
            foreach (int length in Enumerable.Range(1, 320))
            {
                string body = new string('£', length - 1) + "@";
                var frames = Encode(command, body, "gsm7", packed: true);
                Assert.That(string.Concat(frames.Select(frame => DecodeGsm(command, frame, true))), Is.EqualTo(body), "Length " + length);
            }
        }

        [TestCase("hello €", "gsm7")]
        [TestCase("Привет 😀", "unicode")]
        public void AutomaticEncodingSelectsGsmOrUnicodeBeforeSending(string body, string expectedFormat)
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection();
            try
            {
                connection.Attach(pair.Local);
                Assert.That(connection.SubmitSm(42, "1", "2", body, "auto", false), Is.Zero);
                string frame = ReadPdu(pair.Remote.GetStream());
                var parsed = new SubmitSm(frame) { UseGsm7 = true };
                parsed.Decode();
                Assert.That(parsed.Body, Is.EqualTo(body));
                Assert.That(parsed.BodyFormat, Is.EqualTo(expectedFormat));
            }
            finally { connection.Quit(); }
        }

        private static string DecodeGsm(string command, string frame, bool packed = false)
        {
            if (command == "submit")
            {
                var message = new SubmitSm(frame) { UseGsm7 = true, Is8bit = packed };
                message.Decode();
                return message.Body;
            }
            if (command == "deliver")
            {
                var message = new DeliverSm(frame) { UseGsm7 = true, Is8bit = packed };
                message.Decode();
                return message.Body;
            }
            var data = new DataSm(frame) { UseGsm7 = true };
            data.Decode();
            return data.Body;
        }
    }
}
