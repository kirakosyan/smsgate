using System;
using System.Text;

namespace Smpp.Requests
{
    public class DataSm : Pdu
    {
        protected string service_type = "";

        protected int source_addr_ton;
        protected int source_addr_npi;
        protected string source_addr;

        protected int dest_addr_ton;
        protected int dest_addr_npi;
        protected string destination_address;

        protected int esm_class;

        protected string registered_delivery = "00";
        protected int data_coding;

        protected string short_message;
        private string multipart_header = "";

        public bool isMultipart => multipart_header.Length != 0;

        public Common.MultipartMessage MultipartMessage => new Common.MultipartMessage
        {
            reference = Convert.ToInt32(multipart_header.Substring(6, 2), 16),
            submitted_dt = DateTime.UtcNow, sender = Sender, recipient = Recipient,
            body_format = BodyFormat, command = Common.CommandId.data_sm,
            num_of_parts = Convert.ToInt32(multipart_header.Substring(8, 2), 16),
            part_num = Convert.ToInt32(multipart_header.Substring(10, 2), 16),
            short_message = short_message
        };

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
                    case 2:
                        return "binary";
                    case 3:
                        return "latin";
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
            var part = MessageBodyCodec.Encode(short_message, data_coding, 2,
                Common.ASCII_MESSAGE_SPLIT_BASE, false, 0)[0];
            var response = new StringBuilder();
            response.Append(BuildHeader(Common.CommandId.data_sm, command_status, sequence_number));
            response.Append(Common.StringToHex(service_type) + "00");
            response.AppendFormat("{0:X2}{1:X2}{2}00", source_addr_ton, source_addr_npi, Common.StringToHex(source_addr));
            response.AppendFormat("{0:X2}{1:X2}{2}00", dest_addr_ton, dest_addr_npi, Common.StringToHex(destination_address));
            response.Append(part.HasUserDataHeader ? "40" : "00");
            response.Append(registered_delivery);
            response.Append(data_coding.ToString("X2"));
            // data_sm has no sm_length; its payload is always a TLV.
            response.Append(part.Field.Substring(2));
            return new[] { (response.Length / 2 + 4).ToString("X8") + response };
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

            tlv.Clear();
            base.LoadTLV(tail);

            multipart_header = "";
            short_message = tlv.TryGetValue("0424", out var payload)
                ? MessageBodyCodec.Decode(payload.ToString(), data_coding, esm_class, false, out multipart_header)
                : string.Empty;
        }
    }
}
