using NUnit.Framework;
using Smpp.Responses;

namespace SmppGate.Unit.Tests.Commands
{
    [TestFixture]
    public class generic_nack_test
    {
        private string rawPdu = "00000010800000000000000000000001";

        [Test]
        public void TestGenericNackEncode()
        {
            var pdu = new GenericNack();
            pdu.sequence_number = 1;

            Assert.That(pdu.Encode(), Is.EqualTo(rawPdu), "generic_nack should be properly encoded");
        }
    }
}
