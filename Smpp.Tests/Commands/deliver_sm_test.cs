using NUnit.Framework;
using Smpp.Requests;

namespace Smpp.Tests.Commands
{
    [TestFixture]
    public class deliver_sm_test
    {
        private string rawPdu =      "00 00 00 46 00 00 00 05 00 00 00 00 00 00 00 03 00 01 01 34 37 39 37 35 30 39 31 38 31 00 00 00 39 31 30 30 00 00 00 00 00 00 00 00 03 00 17 42 53 53 20 54 65 73 74 20 33 30 31 31 31 35 20 6B 6C 20 31 35 35 30";
        private string rawPduLatin = "00 00 00 45 00 00 00 05 00 00 00 00 00 00 00 12 00 01 01 34 37 39 37 35 30 39 31 38 31 00 00 00 39 31 30 30 00 00 00 00 00 00 00 00 03 00 16 4E 6F 72 77 65 67 69 61 6E 20 63 68 61 72 3A 20 E6 F8 E5 C6 D8 C5";

        [Test]
        public void DeliverSmDecode()
        {
            var pdu = new DeliverSm(rawPdu.Replace(" ", ""));
            pdu.Decode();

            Assert.That(pdu.Sender, Is.EqualTo("4797509181"));
            Assert.That(pdu.Body, Is.EqualTo("BSS Test 301115 kl 1550"));
        }

        [Test]
        public void DeliverSmDecodeLatin()
        {
            var pdu = new DeliverSm(rawPduLatin.Replace(" ", ""));
            pdu.Decode();

            Assert.That(pdu.Sender, Is.EqualTo("4797509181"));
            Assert.That(pdu.Body, Is.EqualTo("Norwegian char: æøåÆØÅ"));
        }
    }
}
