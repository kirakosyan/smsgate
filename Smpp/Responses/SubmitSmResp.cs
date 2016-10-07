using System;
using System.Text;

namespace Smpp.Responses
{
    public class SubmitSmResp : Pdu
    {
        protected string message_id;

        public SubmitSmResp(string pdu)
            : base(pdu)
        {
            Decode();
        }

        public SubmitSmResp()
            : base()
        { }

        public string MessageID
        {
            set
            {
                message_id = value;
            }
            get
            {
                return message_id;
            }
        }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.submit_sm_resp, 0, sequence_number));
            response.Append(Common.StringToHex(message_id) + "00");

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }

        public override void Decode()
        {
            string tail = body;
            try
            {
                message_id = tail == String.Empty ? "" : Common.HexToString(Common.GetCString(ref tail));
            }
            catch
            {
                message_id = "";
            }
        }
    }
}
