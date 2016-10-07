using System;
using System.Text;

namespace Smpp.Requests
{
    public class DataSm : Pdu
    {
        protected string service_type;

        protected int source_addr_ton;
        protected int source_addr_npi;
        protected string source_addr;

        protected int dest_addr_ton;
        protected int dest_addr_npi;
        protected string destination_address;

        protected int esm_class;

        protected string registered_delivery;
        protected int data_coding;

        protected string short_message;

        public string Sender
        {
            get
            {
                return source_addr;
            }
            set
            {
                source_addr = value;
            }
        }

        public string Recipient
        {
            get
            {
                return destination_address;
            }
            set
            {
                destination_address = value;
            }
        }

        public string Body
        {
            set
            {
                short_message = value;
            }
            get
            {
                return short_message;
            }
        }

        public string BodyFormat
        {
            set
            {
                data_coding = Common.body_format[value];
            }

            get
            {
                switch (data_coding)
                {
                    case 0:
                    case 1:
                        return "ascii";
                    case 8:
                        return "unicode";
                    case 245:
                        return "wap_push";
                    default:
                        return "ascii";
                }
            }
        }

        public string ServiceType
        {
            set
            {
                service_type = value;
            }
        }

        public int SourceAddrTON
        {
            set
            {
                source_addr_ton = value;
            }
        }

        public int SourceAddrNPI
        {
            set
            {
                source_addr_npi = value;
            }
        }

        public int DestAddrTON
        {
            set
            {
                dest_addr_ton = value;
            }
        }

        public int DestAddrNPI
        {
            set
            {
                dest_addr_npi = value;
            }
        }

        public int RegisteredDelivery
        {
            set
            {
                switch (value)
                {
                    case 0:
                        registered_delivery = "00";
                        break;
                    case 1:
                        registered_delivery = "01";
                        break;
                    case 2:
                        registered_delivery = "02";
                        break;
                    default:
                        registered_delivery = "00";
                        break;
                }
            }
            get
            {
                return int.Parse(registered_delivery);
            }

        }

        public DataSm(string pdu)
            : base(pdu)
        {
            Decode();
        }

        public DataSm()
            : base()
        { }

        public string[] EncodeM()
        {
            // wap-push
            if (data_coding == 245)
            {
                if (!short_message.StartsWith("0605"))
                {
                    var wap = new StringBuilder();
                    string href = "";
                    string rest = "";
                    if (short_message.StartsWith("http://www."))
                    {
                        href = "0D";
                        rest = short_message.Substring(11);
                    }
                    else
                    if (short_message.StartsWith("https://www."))
                    {
                        href = "0F";
                        rest = short_message.Substring(12);
                    }
                    else
                    if (short_message.StartsWith("http://"))
                    {
                        href = "0C";
                        rest = short_message.Substring(7);
                    }
                    else
                    if (short_message.StartsWith("https://"))
                    {
                        href = "0E";
                        rest = short_message.Substring(8);
                    }
                    else
                    if (href == "")
                    {
                        href = "0C";
                        rest = short_message;
                    }
                    //0605040B8423F05F0601AE02056A0045C60C037761702E6A6F622E616D00070103505050000101
                    wap.AppendFormat("0605040B8423F05F0601AE02056A0045C6{0}03{1}00070103{2}000101", href, Common.StringToHex(rest), Common.StringToHex(""));
                    short_message = wap.ToString();
                }
            }


            var response = new StringBuilder();

            response.Append(BuildHeader(Common.CommandId.data_sm, 0, sequence_number));

            var sb = new StringBuilder();

            //sb.Append(Common.StringToHex(service_type) + "00");
            sb.Append("00");
            sb.Append(source_addr_ton.ToString("X2"));
            sb.Append(source_addr_npi.ToString("X2"));
            sb.Append(Common.StringToHex(source_addr) + "00");

            sb.Append(dest_addr_ton.ToString("X2"));
            sb.Append(dest_addr_npi.ToString("X2"));
            sb.Append(Common.StringToHex(destination_address) + "00");

            if (data_coding == 245 || Common.StringToHex(short_message).StartsWith("050003"))
            {
                sb.Append("40"); // esm_class
            }
            else
            {
                sb.Append("00");
            }

            sb.Append(registered_delivery); // registered_delivery
            sb.Append(data_coding.ToString("X2")); // data_coding

            if (data_coding == 0 || data_coding == 1)
            {
                sb.Append("0424");
                sb.Append(short_message.Length.ToString("X4"));
                sb.Append(Common.StringToHex(short_message));
            }
            if (data_coding == 8)
            {
                var be = new StringBuilder();
                foreach (char c in short_message)
                {
                    be.Append(((int)c).ToString("X4"));
                }
                sb.Append("0424");
                sb.AppendFormat("{0:X4}", short_message.Length * 2);
                sb.Append(be);
            }

            if (data_coding == 245)
            {
                sb.AppendFormat("0424{0:X4}{1}", short_message.Length / 2, short_message);
            }

            response.Append(sb);

            int len = response.Length / 2 + 4;

            short_message = len.ToString("X8") + response;

            string[] part_message = new string[1];
            part_message[0] = short_message;
            return part_message;
        }

        public override void Decode()
        {
            string tail = body;
            byte[] bt;

            service_type = Common.HexToString(Common.GetCString(ref tail));

            bt = Common.ConvertHexStringToByteArray(tail.Substring(0, 4));
            tail = tail.Remove(0, 4);
            source_addr_ton = bt[0];
            source_addr_npi = bt[1];
            source_addr = Common.HexToString(Common.GetCString(ref tail));

            bt = Common.ConvertHexStringToByteArray(tail.Substring(0, 4));
            tail = tail.Remove(0, 4);
            dest_addr_ton = bt[0];
            dest_addr_npi = bt[1];
            destination_address = Common.HexToString(Common.GetCString(ref tail));

            bt = Common.ConvertHexStringToByteArray(tail.Substring(0, 6));
            tail = tail.Remove(0, 6);
            esm_class = bt[0];
            registered_delivery = bt[1].ToString();
            data_coding = bt[2];

            base.LoadTLV(tail);

            //message_payload
            if (tlv.ContainsKey("0424"))
            {
                switch (data_coding)
                {
                    // UNICODE
                    case 8:
                        short_message = Encoding.BigEndianUnicode.GetString(
                         Common.ConvertHexStringToByteArray(tlv["0424"].ToString())
                        );
                        break;
                    //ASCII
                    case 1:
                    case 0:
                        short_message = Common.HexToString(tlv["0424"].ToString());
                        break;
                    default:
                        short_message = Common.HexToString(tlv["0424"].ToString());
                        break;
                }
            }

            if (short_message == null)
            {
                short_message = String.Empty;
            }

            //tail = tail.Remove(0, sm_length * 2);
        }
    }
}
