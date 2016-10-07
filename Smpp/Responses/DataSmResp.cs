using System.Text;

namespace Smpp.Responses
{
    public class DataSmResp : SubmitSmResp
    {
        public DataSmResp(string pdu) : base(pdu)
        {

        }
        public DataSmResp()
            : base()
        { }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.data_sm_resp, 0, sequence_number));
            response.Append(Common.StringToHex(message_id) + "00");

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
