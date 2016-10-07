using System;
using System.Text;

namespace Smpp.Responses
{
    public class DeliverSmResp : Pdu
    {
        private string message_id;

        public DeliverSmResp(string pdu)
            : base(pdu)
        {
            Decode();
        }

        public DeliverSmResp()
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

            response.Append(BuildHeader(Common.CommandId.deliver_sm_resp, 0, sequence_number));
            response.Append("00");

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }

        public override void Decode()
        {
            string tail = body;
            try
            {
                if (tail == String.Empty)
                {
                    message_id = "";
                }
                else
                {
                    message_id = Common.HexToString(Common.GetCString(ref tail));
                }
            }
            catch
            {
                message_id = "";
            }
        }
    }
}
