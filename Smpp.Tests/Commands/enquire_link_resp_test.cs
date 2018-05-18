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

            Assert.AreEqual(rawPdu, pdu.Encode(), "enquire_link_resp should be properly encoded");
        }

        [Test]
        public void TestEnquireLinkRespDecode()
        {
            var pdu = new EnquireLinkResp(rawPdu);

            Assert.AreEqual(1, (int)pdu.sequence_number, "enquire_link_resp sequence number should match");
            Assert.AreEqual((uint)0x80000015, (uint)pdu.command_id, "Command_id should be enquire_link_resp");
        }
    }
}
