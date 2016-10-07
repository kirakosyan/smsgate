using System.Text;

namespace Smpp.Responses
{
    public class GenericNack : Pdu
    {
        public GenericNack()
            : base()
        {
        }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.generic_nack, 0, sequence_number));

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
