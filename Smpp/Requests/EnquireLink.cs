using System.Text;

namespace Smpp.Requests
{
    public class EnquireLink : Pdu
    {
        public EnquireLink(string pdu)
            : base(pdu)
        {
        }

        public EnquireLink()
            : base()
        {
        }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.enquire_link, 0, sequence_number));

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
