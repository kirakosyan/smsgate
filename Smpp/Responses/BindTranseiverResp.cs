using System.Text;

namespace Smpp.Responses
{
    public class BindTransceiverResp : Pdu
    {
        private string _system_id = string.Empty;

        public BindTransceiverResp()
            : base()
        {
        }

        public BindTransceiverResp(string pdu)
            : base(pdu)
        {

        }

        public string SystemID
        {
            get
            {
                return _system_id;
            }
            set
            {
                _system_id = (value == null ? "00" : Common.StringToHex(value));
            }
        }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.bind_transceiver_resp, command_status, sequence_number));
            response.Append("00");

            // Some systems does not like this, ignore
            //response.Append(SystemID);

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
