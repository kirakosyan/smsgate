using System.Text;

namespace Smpp.Responses
{
    public class EnquireLinkResp : Pdu
    {
        public EnquireLinkResp()
            : base()
        {
        }

        public EnquireLinkResp(string pdu)
            : base(pdu)
        { }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.enquire_link_resp, 0, sequence_number));

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
