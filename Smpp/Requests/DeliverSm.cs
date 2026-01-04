using System;
using System.Text;
using System.Text.RegularExpressions;
using Smpp.Events;

namespace Smpp.Requests
{
    public class DeliverSm : Pdu
    {
        private string reference_from;

        private string service_type;

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

        private string registered_delivery;
        private int replace_if_present_flag;
        private int data_coding;
        private int sm_default_msg_id;

        private int sm_length;
        private string short_message;
        private string body_hex;

        private string delivery_status;
        protected bool is_8bit;

        protected int large_message_handle_method;

        protected string multipart_header = "";

        protected bool is_delivery_report;

        protected int split_base_ascii;

        private GateEvents gateEvents;

        public DeliverSm(GateEvents events)
        {
            gateEvents = events;
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
                return int.Parse(registered_delivery);
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
                    case 1:
                        return "ascii";
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
        public Common.MultipartMessage MultipartMessage
        {
            get
            {
                var mes = new Common.MultipartMessage
                {
                    reference = int.Parse(multipart_header.Substring(6, 2), System.Globalization.NumberStyles.HexNumber),
                    submitted_dt = DateTime.Now,
                    num_of_parts =
                        int.Parse(multipart_header.Substring(8, 2), System.Globalization.NumberStyles.HexNumber),
                    part_num = int.Parse(multipart_header.Substring(10, 2), System.Globalization.NumberStyles.HexNumber),
                    short_message = short_message
                };

                return mes;
            }
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
            if (sm_length != 0)
            {
                switch (data_coding)
                {
                    case 8:// UNICODE
                        body_hex = tail.Substring(0, sm_length * 2);
                        if (body_hex.StartsWith("050003"))
                        {
                            multipart_header = body_hex.Substring(0, 12);
                            short_message = Encoding.BigEndianUnicode.GetString(Common.ConvertHexStringToByteArray(tail.Substring(12, sm_length * 2 - 12)));
                        }
                        else
                        {
                            if (esm_class != 4)
                            {
                                short_message = Encoding.BigEndianUnicode.GetString(Common.ConvertHexStringToByteArray(body_hex));
                            }
                            else
                            {
                                short_message = Encoding.Unicode.GetString(Common.ConvertHexStringToByteArray(body_hex));
                            }
                        }
                        break;
                    case 3: // Latin 1 (ISO-8859-1)
                    case 1: // ASCII
                    case 0:
                        body_hex = tail.Substring(0, sm_length * 2);
                        if (is_8bit)
                        {
                            short_message = Common.Convert8bitTo7bit(body_hex);
                        }
                        else
                        {
                            if (body_hex.StartsWith("050003"))
                            {
                                multipart_header = body_hex.Substring(0, 12);
                                short_message = Common.HexToString(tail.Substring(12, sm_length * 2 - 12));
                            }
                            else
                            {
                                short_message = Common.HexToString(body_hex);
                                //short_message = Encoding.Unicode.GetString(Common.ConvertHexStringToByteArray(body_hex));
                            }
                        }
                        break;
                    default:
                        body_hex = tail.Substring(0, sm_length * 2);
                        short_message = body_hex;
                        break;
                }
            }
            else
            {
                base.LoadTLV(tail);

                //message_payload
                if (tlv.ContainsKey("0424"))
                {
                    switch (data_coding)
                    {
                        // UNICODE
                        case 8:
                            body_hex = tlv["0424"].ToString();
                            short_message = Encoding.BigEndianUnicode.GetString(
                             Common.ConvertHexStringToByteArray(body_hex)
                            );
                            break;
                        //ASCII
                        case 1:
                        case 0:
                            body_hex = tlv["0424"].ToString();
                            short_message = Common.HexToString(body_hex);
                            break;
                        default:
                            body_hex = tlv["0424"].ToString();
                            short_message = Common.HexToString(body_hex);
                            break;
                    }
                }
            }

            if (short_message == null)
            {
                short_message = String.Empty;
            }

            // example from SMSinform - megafon
            // id:32C84E82 sub:001 dlvrd:001 submit date:0709111120 done date:0709111120 stat:DELIVRD'
            // Delivery report
            if (esm_class == 4)
            {
                int i_status;

                var reg = Regex.Match(short_message, @"id:\s*([a-zA-Z\-0-9]+)", RegexOptions.IgnoreCase);
                if (reg.Groups.Count == 2)
                {
                    reference_from = reg.Groups[1].Value;
                }


                if (reference_from == String.Empty)
                {
                    reference_from = "";
                }

                i_status = short_message.IndexOf("stat:", StringComparison.CurrentCultureIgnoreCase);
                if (i_status == -1)
                {
                    //gateEvents.LogEvent(LogEvent.Level.Error, "Error decoding delivery report: no reference; " + short_message);
                    delivery_status = "DELIVR";
                    return;
                }

                //delivery_status = short_message.Substring(i_status + 5, i_p - i_status - 5, 7);

                // in common SMSC returns 7 symbols , but some just 6 
                // so let for first 6 symbols
                try
                {
                    delivery_status = short_message.Substring(i_status + 5, 6);
                }
                catch
                {
                    delivery_status = String.Empty;
                    gateEvents.LogEvent(LogEvent.Level.Error, "Delivery error code new " + short_message);
                }

                if (delivery_status == String.Empty)
                {
                    delivery_status = "DELIVR";
                }
            }
        }

        public virtual string[] EncodeM()
        {
            bool is_multipart = false;
            int part_num = 1;

            byte multipart_ref_number;

            Random r = new Random();

            multipart_ref_number = (byte)r.Next(1, 255);

            if (data_coding == 0 || data_coding == 1)
            {
                if (short_message.Length < Common.SINGLE_ASCII_MESSAGE_LENGTH + 1)
                {
                    is_multipart = false;
                }
                else
                {
                    if (large_message_handle_method == 3)
                    {
                        short_message = short_message.Substring(0, Common.SINGLE_ASCII_MESSAGE_LENGTH);
                    }
                    if (large_message_handle_method == 1)
                    {
                        is_multipart = true;
                        if (short_message.Length % split_base_ascii != 0)
                        {
                            part_num = short_message.Length / split_base_ascii + 1;
                        }
                        else
                        {
                            part_num = short_message.Length / split_base_ascii;
                        }
                    }
                }
            }
            if (data_coding == 8)
            {
                if (short_message.Length < 71)
                {
                    is_multipart = false;
                }
                else
                {
                    if (large_message_handle_method == 1)
                    {
                        is_multipart = true;
                        if (short_message.Length % Common.UNICODE_MESSAGE_SPLIT_BASE != 0)
                        {
                            part_num = short_message.Length / Common.UNICODE_MESSAGE_SPLIT_BASE + 1;
                        }
                        else
                        {
                            part_num = short_message.Length / Common.UNICODE_MESSAGE_SPLIT_BASE;
                        }
                    }
                    if (large_message_handle_method == 3)
                    {
                        short_message = short_message.Substring(0, Common.SINGLE_UNICODE_MESSAGE_LENGTH);
                    }
                }
            }

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

            string[] parts = new string[part_num];
            for (int part = 1; part <= part_num; part++)
            {
                StringBuilder response = new StringBuilder();
                int len = 0;
                string part_message = "";
                if (data_coding == 1 || data_coding == 0)
                {
                    if (is_multipart)
                    {
                        if (!is_8bit)
                        {
                            part_message = short_message.Substring((part - 1) * split_base_ascii,
                                short_message.Substring((part - 1) * split_base_ascii + (part == 1 ? 0 : 1)).Length > split_base_ascii ? split_base_ascii : short_message.Substring((part - 1) * split_base_ascii).Length);
                        }
                        else
                        {
                            part_message = Common.HexToString("00000000000000") + short_message.Substring((part - 1) * split_base_ascii,
                                short_message.Substring((part - 1) * split_base_ascii + (part == 1 ? 0 : 1)).Length > split_base_ascii ? split_base_ascii : short_message.Substring((part - 1) * split_base_ascii).Length);
                        }
                    }
                    else
                    {
                        part_message = short_message;
                    }
                }
                if (data_coding == 8)
                {
                    if (is_multipart)
                    {
                        var segmentLength = Common.UNICODE_MESSAGE_SPLIT_BASE;
                        var startIndex = (part - 1) * segmentLength;
                        var availableLength = Math.Max(0, short_message.Length - startIndex);
                        var takeLength = Math.Min(segmentLength, availableLength);
                        part_message = availableLength > 0
                            ? short_message.Substring(startIndex, takeLength)
                            : string.Empty;
                    }
                    else
                    {
                        part_message = short_message;
                    }
                }
                if (data_coding == 245)
                {
                    part_message = short_message;
                }

                response.Append(BuildHeader(Common.CommandId.deliver_sm, 0, sequence_number + (uint)part - (uint)1));
                sm_length = part_message.Length;
                var sb = new StringBuilder();

                sb.Append("00");
                sb.Append(source_addr_ton.ToString("X2"));
                sb.Append(source_addr_npi.ToString("X2"));
                sb.Append(Common.StringToHex(source_addr) + "00");

                sb.Append(dest_addr_ton.ToString("X2"));
                sb.Append(dest_addr_npi.ToString("X2"));
                sb.Append(Common.StringToHex(destination_address) + "00");

                if (!is_delivery_report)
                {
                    if (is_multipart || data_coding == 245 || Common.StringToHex(part_message).StartsWith("050003"))
                    {
                        sb.Append("40"); // esm_class
                    }
                    else
                    {
                        sb.Append("00");
                    }
                }
                else
                {
                    sb.Append("04");
                }

                sb.Append("00"); // protocol_id
                sb.Append("00"); // priority_flag

                sb.Append("00"); // schedule_delivery_time
                sb.Append("00"); // validity_period

                sb.Append(registered_delivery); // registered_delivery
                sb.Append("00"); // replace_if_present_flag
                sb.Append(data_coding.ToString("X2")); // data_coding
                sb.Append("00"); // sm_default_msg_id

                if (data_coding == 0 || data_coding == 1)
                {
                    if (!is_multipart)
                    {
                        if (is_8bit)
                        {
                            string _8bit = Common.Convert7bitTo8bit(part_message);
                            _8bit = Common.StringToHex8Bit(_8bit);
                            sm_length = _8bit.Length / 2;
                            sb.Append(sm_length.ToString("X2"));
                            sb.Append(_8bit);
                        }
                        else
                        {
                            if (large_message_handle_method == 2)
                            {
                                sb.Append("00");
                                sb.Append("0424");
                                sb.Append(sm_length.ToString("X4"));
                                sb.Append(Common.StringToHex(part_message));
                            }
                            else
                            {
                                sb.Append(sm_length.ToString("X2"));
                                sb.Append(Common.StringToHex(part_message));
                            }
                        }
                    }
                    else
                    {
                        if (!is_8bit)
                        {
                            sm_length = sm_length + 6;
                            sb.Append(sm_length.ToString("X2"));
                            sb.Append(
                                "05" +
                                "00" +
                                "03" +
                                multipart_ref_number.ToString("X2") +
                                part_num.ToString("D2") +
                                part.ToString("D2") +
                                Common.StringToHex(part_message));
                        }
                        else
                        {
                            string _8bit;
                            _8bit = Common.Convert7bitTo8bit(part_message);
                            _8bit = Common.StringToHex8Bit(_8bit);
                            if (_8bit.StartsWith("000000000000"))
                            {
                                _8bit = _8bit.Remove(0, 12);
                            }

                            sm_length = _8bit.Length / 2 + 6;
                            sb.Append(sm_length.ToString("X2"));
                            sb.Append(
                                "05" +
                                "00" +
                                "03" +
                                multipart_ref_number.ToString("X2") +
                                part_num.ToString("D2") +
                                part.ToString("D2") +
                                _8bit);
                        }
                    }
                }
                if (data_coding == 8)
                {
                    var be = new StringBuilder();
                    foreach (char c in part_message)
                    {
                        be.Append(((int)c).ToString("X4"));
                    }
                    if (!is_multipart)
                    {
                        if (large_message_handle_method == 2)
                        {
                            sb.Append("00");
                            sb.Append("0424");
                            sm_length = sm_length * 2;
                            sb.Append(sm_length.ToString("X4"));
                            sb.Append(be);
                        }
                        else
                        {
                            sm_length = sm_length * 2;
                            sb.Append(sm_length.ToString("X2"));
                            sb.Append(be);
                        }
                    }
                    else
                    {
                        sm_length = sm_length * 2 + 6;
                        sb.Append(sm_length.ToString("X2"));
                        sb.Append(
                            "05" +
                            "00" +
                            "03" +
                            multipart_ref_number.ToString("X2") +
                            part_num.ToString("D2") +
                            part.ToString("D2") +
                            be);
                    }
                }

                if (data_coding == 245)
                {
                    sb.AppendFormat("{0:X2}{1}", part_message.Length / 2, part_message);
                }

                response.Append(sb);

                len = response.Length / 2 + 4;

                parts[part - 1] = len.ToString("X8") + response;
            }

            return parts;
        }
    }
}
