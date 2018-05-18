using NUnit.Framework;
using Smpp.Requests;

namespace Smpp.Tests.Commands
{
    [TestFixture]
    public class enquire_link_test
    {
        private string rawPdu = "00000010000000150000000000000001";

        [Test]
        public void TestEnquireLinkEncode()
        {
            var pdu = new EnquireLink();
            pdu.sequence_number = 1;

            Assert.AreEqual(rawPdu, pdu.Encode(), "enquire_link should be properly encoded");
        }

        [Test]
        public void TestEnquireLinkDecode()
        {
            var pdu = new EnquireLink(rawPdu);

            Assert.AreEqual(1, (int)pdu.sequence_number, "enquire_link sequence number should match");
            Assert.AreEqual((uint)0x00000015, (uint)pdu.command_id, "Command_id should be enquire_link");
        }
    }
}
