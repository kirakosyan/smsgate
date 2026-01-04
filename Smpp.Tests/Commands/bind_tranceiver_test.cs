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

            Assert.That(pdu.Encode(), Is.EqualTo(rawPdu), "bind_tranceiver should be encoded properly");
        }

        [Test]
        public void TestBindTranceiverDecode()
        {
            var pdu = new BindTransceiver(rawPdu);

            Assert.That((uint)pdu.command_id, Is.EqualTo((uint)0x00000009), "Command_id should be bind_tranceiver");
            Assert.That(pdu.SystemID, Is.EqualTo(systemId), "SystemId Should match");
            Assert.That(pdu.Password, Is.EqualTo(password), "Password should match");
            Assert.That(pdu.SystemType, Is.EqualTo(systemType), "System types should match");
        }
    }
}
