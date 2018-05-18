using NUnit.Framework;
using Smpp.Requests;

namespace Smpp.Tests.Commands
{
    [TestFixture]
    public class bind_tranceiver_test
    {
        private string rawPdu = "0000002900000009000000000000000073797374656D4964004D79506173730045534D450034010100";

        private string systemId = "systemId";
        private string password = "MyPass";
        private string systemType = "ESME";

        [Test]
        public void TestBindTranceiverEncode()
        {
            var pdu = new BindTransceiver();
            pdu.SystemID = systemId;
            pdu.Password = password;
            pdu.SystemType = systemType;

            Assert.AreEqual(rawPdu, pdu.Encode(), "bind_tranceiver should be encoded properly");
        }

        [Test]
        public void TestBindTranceiverDecode()
        {
            var pdu = new BindTransceiver(rawPdu);

            Assert.AreEqual((uint)0x00000009, (uint)pdu.command_id, "Command_id should be bind_tranceiver");
            Assert.AreEqual(systemId, pdu.SystemID, "SystemId Should match");
            Assert.AreEqual(password, pdu.Password, "Password should match");
            Assert.AreEqual(systemType, pdu.SystemType, "System types should match");
        }
    }
}
