using NUnit.Framework;
using Smpp.Responses;

namespace Smpp.Tests.Commands
{
    [TestFixture]
    public class enquire_link_resp_test
    {
        private string rawPdu = "00000010800000150000000000000001";                    

        [Test]
        public void TestEnquireLinkRespEncode()
        {
            var pdu = new EnquireLinkResp();
            pdu.sequence_number = 1;

            Assert.That(pdu.Encode(), Is.EqualTo(rawPdu), "enquire_link_resp should be properly encoded");
        }

        [Test]
        public void TestEnquireLinkRespDecode()
        {
            var pdu = new EnquireLinkResp(rawPdu);

            Assert.That((int)pdu.sequence_number, Is.EqualTo(1), "enquire_link_resp sequence number should match");
            Assert.That((uint)pdu.command_id, Is.EqualTo((uint)0x80000015), "Command_id should be enquire_link_resp");
        }
    }
}
