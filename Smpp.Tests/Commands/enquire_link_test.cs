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

            Assert.That(pdu.Encode(), Is.EqualTo(rawPdu), "enquire_link should be properly encoded");
        }

        [Test]
        public void TestEnquireLinkDecode()
        {
            var pdu = new EnquireLink(rawPdu);

            Assert.That((int)pdu.sequence_number, Is.EqualTo(1), "enquire_link sequence number should match");
            Assert.That((uint)pdu.command_id, Is.EqualTo((uint)0x00000015), "Command_id should be enquire_link");
        }
    }
}
