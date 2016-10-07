using System.Text;

namespace Smpp.Responses
{
    public class BindTransmitterResp : Pdu
    {
        private string _system_id = string.Empty;

        public BindTransmitterResp() : base()
        {
        }

        public BindTransmitterResp(string pdu)
            : base(pdu)
        { }

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

            response.Append(BuildHeader(Common.CommandId.bind_transmitter_resp, 0, sequence_number));
            response.Append("00");

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }
    }
}
