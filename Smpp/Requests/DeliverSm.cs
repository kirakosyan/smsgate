using System;
using System.Text;
using System.Collections.Generic;
using Smpp.Events;

namespace Smpp.Requests
{
    public class DeliverSm : Pdu
    {
        private string reference_from;

        private string service_type = "";

        private int source_addr_ton;
        private int source_addr_npi;
        private string source_addr;

        private int dest_addr_ton;
        private int dest_addr_npi;
        private string destination_address;

        private int esm_class;
        private int protocol_id;
        private int priority_flag;

        private string schedule_delivery_time;
        private string validity_period;

        private string registered_delivery = "00";
        private int replace_if_present_flag;
        private int data_coding;
        private int sm_default_msg_id;

        private int sm_length;
        private string short_message;

        private string delivery_status;
        protected bool is_8bit;

        public bool UseGsm7 { get; set; }

        protected int large_message_handle_method = 1;

        protected string multipart_header = "";

        protected bool is_delivery_report;

        protected int split_base_ascii = Common.ASCII_MESSAGE_SPLIT_BASE;

        public byte MultipartSequenceNumber { get; set; } = (byte)RuntimeCompatibility.NextMultipartReference();


        public DeliverSm(GateEvents events)
        {
            // Kept for API compatibility; malformed receipts are reported by the connection.
        }

        public DeliverSm(string pdu)
            : base(pdu)
        {
            // Do not decode upon receive, 8 bit decoding flag can be required
            // Decode();
        }

        public bool IsDeliveryReport
        {
            get
            {
                return is_delivery_report;
            }
            set
            {
                is_delivery_report = value;
                registered_delivery = "00";
            }
        }

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

        public string Body
        {
            get
            {
                return short_message;
            }
            set
            {
                short_message = value;
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
                return Convert.ToInt32(registered_delivery, 16);
            }
        }

        public string Reference
        {
            set
            {
                reference_from = value;
            }
            get
            {
                return reference_from;
            }
        }

        public int ESMClass
        {
            get
            {
                return esm_class;
            }
            set
            {
                esm_class = value;
            }
        }

        public string DeliveryStatus
        {
            get
            {
                return delivery_status;
            }
        }

        public string BodyFormat
        {
            get
            {
                switch (data_coding)
                {
                    case 0:
                        return UseGsm7 ? "gsm7" : "ascii";
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
            set
            {
                data_coding = Common.body_format[value];
                UseGsm7 = value == "gsm7";
            }
        }

        public bool Is8bit
        {
            set
            {
                is_8bit = value;
            }
        }

        public int SplitBaseASCII
        {
            set
            {
                split_base_ascii = value;
            }
            get
            {
                return split_base_ascii;
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
        public Common.MultipartMessage MultipartMessage => MessageBodyCodec.Multipart(multipart_header, tlv,
            Common.CommandId.deliver_sm, Sender, Recipient, BodyFormat, short_message);

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
            registered_delivery = bt[0].ToString("X2");
            replace_if_present_flag = bt[1];
            data_coding = bt[2];
            sm_default_msg_id = bt[3];

            sm_length = bt[4];
            string shortMessageHex = tail.Substring(0, sm_length * 2);
            string messageHex = shortMessageHex;
            tail = tail.Substring(sm_length * 2);
            tlv.Clear();
            LoadTLV(tail);
            if (sm_length == 0 && tlv.TryGetValue("0424", out var payload))
            {
                messageHex = payload.ToString();
                large_message_handle_method = 2;
            }
            short_message = MessageBodyCodec.Decode(messageHex, data_coding, esm_class, is_8bit, out multipart_header, UseGsm7, tlv);
            RememberWireBody(CurrentEncoding, shortMessageHex, esm_class);
            ParseDeliveryReport();
        }

        internal DeliveryReceipt ParsedReceipt { get; private set; }
        internal List<string> ReceiptWarnings { get; } = new();
        internal void ParseDeliveryReport()
        {
            reference_from = delivery_status = "";
            ParsedReceipt = null;
            ReceiptWarnings.Clear();
            if ((esm_class & 0x3C) != 4) return;
            ParsedReceipt = DeliveryReceiptParser.Parse("", short_message, tlv, ReceiptWarnings.Add);
            if (ParsedReceipt != null)
            {
                reference_from = ParsedReceipt.ProviderMessageId;
                delivery_status = DeliveryReceiptParser.StateText(ParsedReceipt.MessageState, legacy: true);
            }
        }

        private BodyEncoding CurrentEncoding => new(short_message, data_coding,
            large_message_handle_method, split_base_ascii, is_8bit, MultipartSequenceNumber, UseGsm7);

        public virtual string[] EncodeM()
        {
            var encoded = EncodeBody(CurrentEncoding, out bool originalBody);
            var parts = new string[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
            {
                var part = encoded[i];
                var response = new StringBuilder();
                response.Append(BuildHeader(Common.CommandId.deliver_sm, command_status, sequence_number + (uint)i));
                response.Append(Common.StringToHex(service_type) + "00");
                response.AppendFormat("{0:X2}{1:X2}{2}00", source_addr_ton, source_addr_npi, Common.StringToHex(source_addr));
                response.AppendFormat("{0:X2}{1:X2}{2}00", dest_addr_ton, dest_addr_npi, Common.StringToHex(destination_address));
                response.AppendFormat("{0:X2}{1:X2}{2:X2}", EncodeEsmClass(esm_class, part, originalBody) | (is_delivery_report ? 4 : 0), protocol_id, priority_flag);
                response.Append(Common.StringToHex(schedule_delivery_time ?? "") + "00");
                response.Append(Common.StringToHex(validity_period ?? "") + "00");
                response.Append(registered_delivery);
                response.AppendFormat("{0:X2}{1:X2}{2:X2}", replace_if_present_flag, data_coding, sm_default_msg_id);
                response.Append(part.Field);
                response.Append(EncodeMessageParameters(!originalBody && large_message_handle_method == 2, part.Field != "00", originalBody));
                parts[i] = (response.Length / 2 + 4).ToString("X8") + response;
            }
            return parts;
        }

    }
}
