using System.Text;

namespace Smpp.Requests
{
    public class BindTransmitter : Pdu
    {
        #region private

        private string _system_id = string.Empty;
        private string _password = string.Empty;
        private string _system_type = string.Empty;
        private string _address_range = string.Empty;

        private Common.SmppVersionType _interface_version = Common.SmppVersionType.Version3_4;
        private Common.TonType _ton = Common.TonType.International;
        private Common.NpiType _npi = Common.NpiType.ISDN;

        #endregion

        #region constants

        private const int ID_LENGTH = 15;
        private const int PASS_LENGTH = 8;
        private const int TYPE_LENGTH = 12;
        private const int RANGE_LENGTH = 40;

        #endregion constants

        #region enumerations

        /// <summary>
        /// Binding types for the SMPP bind request.
        /// </summary>
        public enum BindingType : uint
        {
            /// <summary>
            /// BindAsReceiver
            /// </summary>
            BindAsReceiver = 1,
            /// <summary>
            /// BindAsTransmitter
            /// </summary>
            BindAsTransmitter = 2,
            /// <summary>
            /// BindAsTransceiver
            /// </summary>
            BindAsTransceiver = 9
        }

        #endregion enumerations

        public BindTransmitter(string pdu) : base(pdu)
        {
            Decode();
        }

        public BindTransmitter()
        {
        }

        #region mandatory parameters

        /// <summary>
        /// The ESME system requesting to bind with the SMSC.  Set to null for default value.
        /// </summary>
        public string SystemID
        {
            get
            {
                return _system_id;
            }
            set
            {
                _system_id = value;
            }
        }

        /// <summary>
        /// Used to authenticate ESME. Set to null for default
        /// </summary>
        public string Password
        {
            get
            {
                return _password;
            }
            set
            {
                _password = value;
            }
        }

        public string SystemType
        {
            get
            {
                return _system_type;
            }
            set
            {
                _system_type = value;
            }
        }

        public string AddressRange
        {
            get
            {
                return _address_range;
            }
        }

        public Common.SmppVersionType InterfaceVersion
        {
            get
            {
                return _interface_version;
            }
            set
            {
                _interface_version = value;
            }
        }

        public Common.TonType Ton
        {
            get
            {
                return _ton;
            }
            set
            {
                _ton = value;
            }
        }

        public Common.NpiType Npi
        {
            get
            {
                return _npi;
            }
            set
            {
                _npi = value;
            }
        }

        #endregion

        public override void Decode()
        {
            string tail = body;
            byte[] bt;

            _system_id = Common.HexToString(Common.GetCString(ref tail));
            _password = Common.HexToString(Common.GetCString(ref tail));
            _system_type = Common.HexToString(Common.GetCString(ref tail));

            bt = Common.ConvertHexStringToByteArray(tail);
            _interface_version = (Common.SmppVersionType)bt[0];
            _ton = (Common.TonType)bt[1];
            _npi = (Common.NpiType)bt[2];
        }

        public override string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader(Common.CommandId.bind_transmitter, 0, sequence_number));

            var sb = new StringBuilder();
            sb.Append(Common.StringToHex(SystemID) + "00");
            sb.Append(Common.StringToHex(Password) + "00");
            sb.Append(Common.StringToHex(SystemType) + "00");
            sb.Append("34");
            sb.Append(((int)Ton).ToString("X2"));
            sb.Append(((int)Npi).ToString("X2"));
            sb.Append("00"); // address range

            response.Append(sb);

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }

    }
}
