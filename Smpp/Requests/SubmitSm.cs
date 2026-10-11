using System;
using System.Text;

namespace Smpp.Requests
{
    public class SubmitSm : Pdu
    {
        protected string service_type = "";

        protected int source_addr_ton;
        protected int source_addr_npi;
        protected string source_addr;

        protected int dest_addr_ton;
        protected int dest_addr_npi;
        protected string destination_address;

        protected int esm_class;
        protected int protocol_id;
        protected int priority_flag;

        protected string schedule_delivery_time;
        protected string validity_period;

        protected string registered_delivery = "00";
        protected int replace_if_present_flag;
        protected int data_coding;
        protected int sm_default_msg_id;

        protected int sm_length;
        protected string short_message;
        protected bool is_8bit;

        protected int split_base_ascii = Common.ASCII_MESSAGE_SPLIT_BASE;

        protected int large_message_handle_method = 1;

        protected string multipart_header = "";
        protected byte multipart_ref_number;

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

        public bool Is8bit
        {
            set
            {
                is_8bit = value;
            }
        }

        public int LargeMessageHandleMethod
        {
            set
            {
                large_message_handle_method = value;
            }
        }

        public bool isMultipart
        {
            get
            {
                if (multipart_header != "")
                    return true;
                else
                    return false;
            }
        }

        public byte MultipartSequenceNumber
        {
            set
            {
                multipart_ref_number = value;
            }
        }

        public int SplitBaseASCII
        {
            get
            {
                return split_base_ascii;
            }
            set
            {
                split_base_ascii = value;
            }
        }

        public Common.MultipartMessage MultipartMessage
        {
            get
            {
                var mes = new Common.MultipartMessage
                {
                    reference = int.Parse(multipart_header.Substring(6, 2), System.Globalization.NumberStyles.HexNumber),
                    submitted_dt = DateTime.UtcNow,
                    sender = Sender,
                    recipient = Recipient,
                    body_format = BodyFormat,
                    command = Common.CommandId.submit_sm,
                    num_of_parts =
                        int.Parse(multipart_header.Substring(8, 2), System.Globalization.NumberStyles.HexNumber),
                    part_num = int.Parse(multipart_header.Substring(10, 2), System.Globalization.NumberStyles.HexNumber),
                    short_message = short_message
                };

                return mes;
            }
        }

        public SubmitSm(string pdu)
            : base(pdu)
        {
            Decode();
            //multipart_header = "";
        }

        public SubmitSm()
            : base()
        {
            is_8bit = false;
        }

        public virtual string[] EncodeM()
        {
            var encoded = MessageBodyCodec.Encode(short_message, data_coding,
                large_message_handle_method, split_base_ascii, is_8bit, multipart_ref_number);
            var parts = new string[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
            {
                var part = encoded[i];
                var response = new StringBuilder();
                response.Append(BuildHeader(Common.CommandId.submit_sm, command_status, sequence_number + (uint)i));
                response.Append(Common.StringToHex(service_type) + "00");
                response.AppendFormat("{0:X2}{1:X2}{2}00", source_addr_ton, source_addr_npi, Common.StringToHex(source_addr));
                response.AppendFormat("{0:X2}{1:X2}{2}00", dest_addr_ton, dest_addr_npi, Common.StringToHex(destination_address));
                response.AppendFormat("{0:X2}00000000", (part.HasUserDataHeader ? 0x40 : 0));
                response.Append(registered_delivery);
                response.AppendFormat("00{0:X2}00", data_coding);
                response.Append(part.Field);
                parts[i] = (response.Length / 2 + 4).ToString("X8") + response;
            }
            return parts;
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
            protocol_id = bt[1];
            priority_flag = bt[2];

            schedule_delivery_time = Common.HexToString(Common.GetCString(ref tail));
            validity_period = Common.HexToString(Common.GetCString(ref tail));

            bt = Common.ConvertHexStringToByteArray(tail.Substring(0, 10));
            tail = tail.Remove(0, 10);
            registered_delivery = bt[0].ToString();
            replace_if_present_flag = bt[1];
            data_coding = bt[2];
            sm_default_msg_id = bt[3];

            sm_length = bt[4];

            string messageHex = tail.Substring(0, sm_length * 2);
            tail = tail.Substring(sm_length * 2);
            tlv.Clear();
            LoadTLV(tail);
            if (sm_length == 0 && tlv.TryGetValue("0424", out var payload))
                messageHex = payload.ToString();
            short_message = MessageBodyCodec.Decode(messageHex, data_coding, esm_class, is_8bit, out multipart_header);
        }

    }
}
