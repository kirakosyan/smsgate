using System;
using System.Collections.Generic;
using System.Text;

namespace Smpp
{
    /// <summary>
    /// The common/helper class
    /// </summary>
    public static class Common
    {
        public static SortedList<uint, string> command_id;
        public static SortedList<uint, CommandDescription> command_status;
        public static SortedList<string, string> tlv;
        public static SortedList<string, int> body_format;

        internal const int SINGLE_ASCII_MESSAGE_LENGTH = 160;
        internal const int SINGLE_UNICODE_MESSAGE_LENGTH = 70;

        internal const int ASCII_MESSAGE_SPLIT_BASE = 153;
        internal const int UNICODE_MESSAGE_SPLIT_BASE = 63;

        static Common()
        {
            InitCommandID();
            InitCommandStatus();
            InitBodyFormat();
            InitTLV();
        }

        private static void InitCommandID()
        {
            command_id = new SortedList<uint, string>();

            command_id.Add(0x80000000, "generic_nack");

            command_id.Add(0x00000001, "bind_receiver");
            command_id.Add(0x80000001, "bind_receiver_resp");

            command_id.Add(0x00000002, "bind_transmitter");
            command_id.Add(0x80000002, "bind_transmitter_resp");

            command_id.Add(0x00000003, "query_sm");
            command_id.Add(0x80000003, "query_sm_resp");

            command_id.Add(0x00000004, "submit_sm");
            command_id.Add(0x80000004, "submit_sm_resp");

            command_id.Add(0x00000005, "deliver_sm");
            command_id.Add(0x80000005, "deliver_sm_resp");

            command_id.Add(0x00000006, "unbind");
            command_id.Add(0x80000006, "unbind_resp");

            command_id.Add(0x00000007, "replace_sm");
            command_id.Add(0x80000007, "replace_sm_resp");

            command_id.Add(0x00000008, "cancel_sm");
            command_id.Add(0x80000008, "cancel_sm_resp");

            command_id.Add(0x00000009, "bind_transceiver");
            command_id.Add(0x80000009, "bind_transceiver_resp");

            // reserved 0x0000000A

            command_id.Add(0x0000000B, "outbind");

            // reserved 0x0000000C - 0x00000014
            // reserved 0x8000000B - 0x80000014

            command_id.Add(0x00000015, "enquire_link");
            command_id.Add(0x80000015, "enquire_link_resp");

            // reserved 0x0000016 - 0x00000020
            // reserved 0x8000016 - 0x80000020

            command_id.Add(0x00000021, "submit_multi");
            command_id.Add(0x80000021, "submit_multi_resp");

            // reserved 0x0000022 - 0x000000FF
            // reserved 0x8000022 - 0x800000FF

            // reserved 0x0000100
            // reserved 0x8000100

            // reserved 0x0000101
            // reserved 0x8000101

            command_id.Add(0x00000102, "alert_notification");
            // reserved 0x8000102

            command_id.Add(0x00000103, "data_sm");
            command_id.Add(0x80000103, "data_sm_resp");

            // reserved for SMPP extension
            // 0x00000104 - 0x0000FFFF
            // 0x80000104 - 0x8000FFFF

            // reserved 0x00010000 - 0x000101FF
            // reserved 0x80010000 - 0x800101FF

            // reserved for SMSC vendor
            // 0x00010200 - 0x000102FF
            // 0x80010200 - 0x800102FF

            // reserved 0x00010300 - 0xFFFFFFFF
        }

        private static void InitCommandStatus()
        {
            command_status = new SortedList<uint, CommandDescription>();
            CommandDescription cmd;

            // no error
            cmd = new CommandDescription();
            cmd.short_code = "ESME_ROK";
            cmd.description = "Ok";
            command_status.Add(0, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVMSGLEN";
            cmd.description = "Message length is invalid";
            command_status.Add(1, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVCMDLEN";
            cmd.description = "Command length is invalid";
            command_status.Add(2, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVCMDLEN";
            cmd.description = "Invalid Command ID";
            command_status.Add(3, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVBNDSTS";
            cmd.description = "Incorrect BIND Status for given command";
            command_status.Add(4, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RALYBND";
            cmd.description = "ESME Already in Bound State";
            command_status.Add(5, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVPRTFLG";
            cmd.description = "Invalid Priority Flag";
            command_status.Add(6, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVREGDLVFLG";
            cmd.description = "Invalid Registered Delivery Flag";
            command_status.Add(7, cmd);

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RSYSERR";
            cmd.description = "System error";
            command_status.Add(8, cmd);

            // 0x9 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSRCADR";
            cmd.description = "Invalid Source Address";
            command_status.Add(10, cmd); //0xA

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDSTADR";
            cmd.description = "Invalid Dest Addr";
            command_status.Add(11, cmd); //0xB

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVMSGID";
            cmd.description = "Message ID is invalid";
            command_status.Add(12, cmd); //0xC

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RBINDFAIL";
            cmd.description = "Bind Failed";
            command_status.Add(13, cmd); //0xD

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVPASWD";
            cmd.description = "Invalid Password";
            command_status.Add(14, cmd); //0xE

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSYSID";
            cmd.description = "Invalid System ID";
            command_status.Add(15, cmd); //0xF

            // 16 - 0x10 Reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RCANCELFAIL";
            cmd.description = "Cancel SM Failed";
            command_status.Add(17, cmd); //0x11

            // 18 - 0x12 Reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RREPLACEFAIL";
            cmd.description = "Replace SM Failed";
            command_status.Add(19, cmd); //0x13

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RMSGQFUL";
            cmd.description = "Message Queue Full";
            command_status.Add(20, cmd); //0x14

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSERTYP";
            cmd.description = "Invalid Service Type";
            command_status.Add(21, cmd); //0x15

            // 0x16 - 0x32 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVNUMDESTS";
            cmd.description = "Invalid number of destinations";
            command_status.Add(51, cmd); //0x33

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDLNAME";
            cmd.description = "Invalid Distribution List name";
            command_status.Add(52, cmd); //0x34

            // 0x35 - 0x3f reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDESTFLAG";
            cmd.description = "Destination flag is invalid (submit_multi)";
            command_status.Add(64, cmd); //0x40

            // 0x41 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSUBREP";
            cmd.description = "Invalid <submit with replace> request";
            command_status.Add(66, cmd); //0x42

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVESMCLASS";
            cmd.description = "Invalid esm_class field data";
            command_status.Add(67, cmd); //0x43

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RCNTSUBDL";
            cmd.description = "Cannot Submit to Distribution List";
            command_status.Add(68, cmd); //0x44

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RSUBMITFAIL";
            cmd.description = "submit_sm or submit_multi failed";
            command_status.Add(69, cmd); //0x45

            // 0x46 - 0x47 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSRCTON";
            cmd.description = "Invalid Source address TON";
            command_status.Add(72, cmd); //0x48

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSRCNPI";
            cmd.description = "Invalid Source address NPI";
            command_status.Add(73, cmd); //0x49

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDSTTON";
            cmd.description = "Invalid Destination address TON";
            command_status.Add(80, cmd); //0x50

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDSTNPI";
            cmd.description = "Invalid Destination address NPI";
            command_status.Add(81, cmd); //0x51

            // 0x52 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSYSTYP";
            cmd.description = "Invalid system_type field";
            command_status.Add(83, cmd); //0x53

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVREPFLAG";
            cmd.description = "Invalid replace_if_present flag";
            command_status.Add(84, cmd); //0x54

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVNUMMSGS";
            cmd.description = "Invalid number of messages";
            command_status.Add(85, cmd); //0x55

            // 0x55 - 0x56 reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RTHROTTLED";
            cmd.description = "Throttling error (ESME has exceeded allowed message limits)";
            command_status.Add(88, cmd); //0x58

            // 0x59 - 0x60

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVSCHED";
            cmd.description = "Invalid Scheduled Delivery Time";
            command_status.Add(97, cmd); //0x61

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVEXPIRY";
            cmd.description = "Invalid message validity period (Expiry time)";
            command_status.Add(98, cmd); //0x62

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVDFTMSGID";
            cmd.description = "Predefined Message Invalid or Not Found";
            command_status.Add(99, cmd); //0x63

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RX_T_APPN";
            cmd.description = "ESME Receiver Temporary App Error Code";
            command_status.Add(100, cmd); //0x64

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RX_P_APPN";
            cmd.description = "ESME Receiver Permanent App Error Code";
            command_status.Add(101, cmd); //0x65

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RX_R_APPN";
            cmd.description = "ESME Receiver Reject Message Error Code";
            command_status.Add(102, cmd); //0x66

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RQUERYFAIL";
            cmd.description = "query_sm request failed";
            command_status.Add(103, cmd); //0x67

            // 0x68 - 0xbf reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVOPTPARSTREAM";
            cmd.description = "Error in the optional part of the PDU Body.";
            command_status.Add(192, cmd); //0xc0

            cmd = new CommandDescription();
            cmd.short_code = "ESME_ROPTPARNOTALLWD";
            cmd.description = "Optional Parameter not allowed";
            command_status.Add(193, cmd); //0xc1

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVPARLEN";
            cmd.description = "Invalid parameter length";
            command_status.Add(194, cmd); //0xc2

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RMISSINGOPTPARAM";
            cmd.description = "Expected Optional Parameter missing";
            command_status.Add(195, cmd); //0xc3

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RINVOPTPARAMVAL";
            cmd.description = "Invalid Optional Parameter Value";
            command_status.Add(196, cmd); //0xc4

            // 0xc5 - 0xfd reserved

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RDELIVERYFAILURE";
            cmd.description = "Delivery Failure (used for data_sm_resp)";
            command_status.Add(254, cmd); //0xfe

            cmd = new CommandDescription();
            cmd.short_code = "ESME_RUNKNOWNERR";
            cmd.description = "Unknown Error";
            command_status.Add(255, cmd); //0xff

            // 0x100 - 0x3ff reserved for SMPP extention

            // 0x400 - 0x4ff reserved for SMSC vendor specific errors
        }

        private static void InitTLV()
        {
            tlv = new SortedList<string, string>();

            tlv.Add("0005", "dest_addr_subunit");   // GSM
            tlv.Add("0006", "dest_network_type");   // Generic
            tlv.Add("0007", "dest_bearer_type");    // Generic
            tlv.Add("0008", "dest_telematics_id");  // GSM
            tlv.Add("000D", "source_addr_subunit"); // GSM
            tlv.Add("000E", "source_network_type"); // Generic
            tlv.Add("000F", "source_bearer_type");  // Generic

            tlv.Add("0010", "source_telematics_id");        // GSM
            tlv.Add("0017", "qos_time_to_live");            // Generic
            tlv.Add("0019", "payload_type");                // Generic
            tlv.Add("001D", "additional_status_info_text"); // Generic
            tlv.Add("001E", "receipted_message_id");        // Generic
            tlv.Add("0030", "ms_msg_wait_facilities");      // GSM

            tlv.Add("0201", "privacy_indicator");       // CDMA, TDMA
            tlv.Add("0202", "source_subaddress");       // CDMA, TDMA
            tlv.Add("0203", "dest_subaddress");         // CDMA, TDMA
            tlv.Add("0204", "usage_message_reference"); // Generic
            tlv.Add("0205", "user_response_code");      // CDMA, TDMA
            tlv.Add("020A", "source_port");             // Generic
            tlv.Add("020B", "destination_port");        // Generic
            tlv.Add("020C", "sar_msg_ref_num");         // Generic
            tlv.Add("020D", "language_indicator");      // CDMA, TDMA
            tlv.Add("020E", "sar_total_segments");      // Generic
            tlv.Add("020F", "sar_segment_seqnum");      // Generic
            tlv.Add("0210", "SC_interface_version");    // Generic

            tlv.Add("0302", "callback_num_pres_ind"); // TDMA
            tlv.Add("0303", "callback_num_atag");     // TDMA
            tlv.Add("0304", "number_of_messages");    // CDMA
            tlv.Add("0381", "callback_num");          // CDMA, TDMA, GSM, iDEN

            tlv.Add("0420", "dpf_result");              // Generic
            tlv.Add("0421", "set_dpf");                 // Generic
            tlv.Add("0422", "ms_availability_status");  // Generic
            tlv.Add("0423", "network_error_code");      // Generic
            tlv.Add("0424", "message_payload");         // Generic
            tlv.Add("0425", "delivery_failure_reason"); // Generic
            tlv.Add("0426", "more_messages_to_send");   // GSM
            tlv.Add("0427", "message_state");           // Generic

            tlv.Add("0501", "ussd_service_op"); // GSM(USSD)

            tlv.Add("1201", "display_time");              // CDMA, TDMA
            tlv.Add("1203", "sms_signal");                // TDMA
            tlv.Add("1204", "ms_validity");               // CDMA, TDMA
            tlv.Add("130C", "alert_on_message_delivery"); // CDMA
            tlv.Add("1380", "its_reply_type");            // CDMA
            tlv.Add("1383", "its_session_info");          // CDMA
        }

        /// <summary>
        /// The structure for a command
        /// </summary>
        public struct CommandDescription
        {
            public string short_code;
            public string description;
        }

        /// <summary>
        /// The structure for building multipart messages
        /// </summary>
        public struct MultipartMessage
        {
            public DateTime submitted_dt;
            public int reference;
            public int part_num;
            public int num_of_parts;
            public string short_message;
        }

        /// <summary>
        /// 5.2.19 data_coding
        /// </summary>
        public static void InitBodyFormat()
        {
            body_format = new SortedList<string, int>();

            body_format.Add("ascii", 0);
            body_format.Add("binary", 2);
            body_format.Add("latin", 3);
            body_format.Add("unicode", 8);
            body_format.Add("wap_push", 245);
        }

        #region constants

        /// <summary>
        /// SMSC identifier
        /// Identifies SMSC to the ESME
        /// </summary>
        public const string SMSC_ID = "SMSC";

        /// <summary>
        /// Standard length of Pdu header.
        /// </summary>
        public const int HEADER_LENGTH = 32;

        /// <summary>
        /// Delivery time length
        /// </summary>
        public const int DATE_TIME_LENGTH = 16;

        /// <summary>
        /// Max buffer length for receiving
        /// </summary>
        public const int MAX_RECEIVE_LENGTH = 1048576; //1Mb

        #endregion

        #region enumerators

        /// <summary>
        /// Enumeration of all the Pdu command types.
        /// </summary>
        public enum CommandId : uint
        {
            /// <summary>
            /// generic_nack
            /// </summary>
            generic_nack = 0x80000000,
            /// <summary>
            /// bind_receiver
            /// </summary>
            bind_receiver = 0x00000001,
            /// <summary>
            /// bind_receiver_resp
            /// </summary>
            bind_receiver_resp = 0x80000001,
            /// <summary>
            /// bind_transmitter
            /// </summary>
            bind_transmitter = 0x00000002,
            /// <summary>
            /// bind_transmitter_resp
            /// </summary>
            bind_transmitter_resp = 0x80000002,
            /// <summary>
            /// query_sm
            /// </summary>
            query_sm = 0x00000003,
            /// <summary>
            /// query_sm_resp
            /// </summary>
            query_sm_resp = 0x80000003,
            /// <summary>
            /// submit_sm
            /// </summary>
            submit_sm = 0x00000004,
            /// <summary>
            /// submit_sm_resp
            /// </summary>
            submit_sm_resp = 0x80000004,
            /// <summary>
            /// deliver_sm
            /// </summary>
            deliver_sm = 0x00000005,
            /// <summary>
            /// deliver_sm_resp
            /// </summary>
            deliver_sm_resp = 0x80000005,
            /// <summary>
            /// unbind
            /// </summary>
            unbind = 0x00000006,
            /// <summary>
            /// unbind_resp
            /// </summary>
            unbind_resp = 0x80000006,
            /// <summary>
            /// replace_sm
            /// </summary>
            replace_sm = 0x00000007,
            /// <summary>
            /// replace_sm_resp
            /// </summary>
            replace_sm_resp = 0x80000007,
            /// <summary>
            /// cancel_sm
            /// </summary>
            cancel_sm = 0x00000008,
            /// <summary>
            /// cancel_sm_resp
            /// </summary>
            cancel_sm_resp = 0x80000008,
            /// <summary>
            /// bind_transceiver
            /// </summary>
            bind_transceiver = 0x00000009,
            /// <summary>
            /// bind_transceiver_resp
            /// </summary>
            bind_transceiver_resp = 0x80000009,
            /// <summary>
            /// outbind
            /// </summary>
            outbind = 0x0000000B,
            /// <summary>
            /// enquire_link
            /// </summary>
            enquire_link = 0x00000015,
            /// <summary>
            /// enquire_link_resp
            /// </summary>
            enquire_link_resp = 0x80000015,
            /// <summary>
            /// submit_multi
            /// </summary>
            submit_multi = 0x00000021,
            /// <summary>
            /// submit_multi_resp
            /// </summary>
            submit_multi_resp = 0x80000021,
            /// <summary>
            /// alert_notification
            /// </summary>
            alert_notification = 0x00000102,
            /// <summary>
            /// data_sm
            /// </summary>
            data_sm = 0x00000103,
            /// <summary>
            /// data_sm_resp
            /// </summary>
            data_sm_resp = 0x80000103
        }

        /// <summary>
        /// SMPP version type.
        /// </summary>
        public enum SmppVersionType : byte
        {
            /// <summary>
            /// Version 3.3 of the SMPP spec.
            /// </summary>
            Version3_3 = 0x33,
            /// <summary>
            /// Version 3.4 of the SMPP spec.
            /// </summary>
            Version3_4 = 0x34
        }

        /// <summary>
        /// Enumerates the type of number types that can be used for the SMSC 
        /// message
        /// sending.
        /// </summary>
        public enum TonType : byte
        {
            /// <summary>
            /// Unknown
            /// </summary>
            Unknown = 0x00,
            /// <summary>
            /// International
            /// </summary>
            International = 0x01,
            /// <summary>
            /// National
            /// </summary>
            National = 0x02,
            /// <summary>
            /// Network specific
            /// </summary>
            NetworkSpecific = 0x03,
            /// <summary>
            /// Subscriber number
            /// </summary>
            SubscriberNumber = 0x04,
            /// <summary>
            /// Alphanumeric
            /// </summary>
            Alphanumeric = 0x05,
            /// <summary>
            /// Abbreviated
            /// </summary>
            Abbreviated = 0x06
        }

        /// <summary>
        /// Enumerates the number plan indicator types that can be used for the 
        /// SMSC
        /// message sending.
        /// </summary>
        public enum NpiType : byte
        {
            /// <summary>
            /// Unknown
            /// </summary>
            Unknown = 0x00,
            /// <summary>
            /// ISDN
            /// </summary>
            ISDN = 0x01,
            /// <summary>
            /// Data
            /// </summary>
            Data = 0x03,
            /// <summary>
            /// Telex
            /// </summary>
            Telex = 0x04,
            /// <summary>
            /// Land mobile
            /// </summary>
            LandMobile = 0x06,
            /// <summary>
            /// National
            /// </summary>
            National = 0x08,
            /// <summary>
            /// Private
            /// </summary>
            Private = 0x09,
            /// <summary>
            /// ERMES
            /// </summary>
            ERMES = 0x0A,
            /// <summary>
            /// Internet
            /// </summary>
            Internet = 0x0E
        }

        /// <summary>
        /// Internal used message statuses
        /// </summary>
        public enum MessageStatus : int
        {
            received_waiting_to_be_processed = 100,
            received_routed = 101,
            received_no_route_specified = 102,
            received_route_failure = 103,
            rejected = 104,
            unknown_recipient = 105,
            unable_to_receive = 107,
            timeout = 108,

            scheduled = 200,
            queued = 201,
            submitted_waiting_for_ACK = 202,
            generic_error = 210,
            no_channel_can_handle_message = 211,
            message_undeliverable = 212,
            NACK_received = 213,
            ACK_expired = 214,
            duplicate_ACK_detected = 215,
            sent = 220,
            delivered_ACK_received = 221,
            out_of_balance = 254,
            locked_by_system = 255
        }

        #endregion

        public static void GetTLV(out string key, out object value, ref string tail)
        {
            int tlv_length = 0;
            key = tail.Substring(0, 4);
            tail = tail.Remove(0, 4);
            tlv_length = Convert.ToInt32(tail.Substring(0, 4), 16);
            value = tail.Substring(4, tlv_length * 2);
            tail = tail.Remove(0, tlv_length * 2 + 4);
        }

        public static string GetCString(ref string tail)
        {
            string result = string.Empty;
            int i = 0;

            while (!(tail[i] == '0' && tail[i + 1] == '0'))
            {
                i += 2;
            }

            //i -= 1;

            result = tail.Substring(0, i);
            tail = tail.Substring(i + 2);

            return result;
        }

        /// <summary>
        /// Converts HEX row to UTF8 string
        /// </summary>
        /// <param name="hex"></param>
        /// <returns>UTF8 string</returns>
        public static string HexToString(string hex)
        {
            return Encoding.GetEncoding("ISO-8859-1").GetString(ConvertHexStringToByteArray(hex));
            //return Encoding.UTF8.GetString(ConvertHexStringToByteArray(hex));
        }

        /// <summary>
        /// Converts string to Hex
        /// </summary>
        /// <param name="str">String to convert</param>
        /// <returns>Hex string</returns>
        public static string StringToHex(string str)
        {
            var sb = new StringBuilder();

            foreach (byte bt in Encoding.GetEncoding("ISO-8859-1").GetBytes(str))
            {
                sb.AppendFormat("{0:X2}", bt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts 8 bit string to HEX
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string StringToHex8Bit(string str)
        {
            int i;
            var sb = new StringBuilder();

            for (i = 0; i < str.Length; i++)
            {
                sb.Append(String.Format("{0:X2}", Convert.ToByte(str[i])));
            }

            return sb.ToString();
        }

        public static string ConvertByteArrayToHexString(byte[] bt)
        {
            return ConvertByteArrayToHexString(bt, bt.Length);
        }

        public static string ConvertByteArrayToHexString(byte[] bt, int length)
        {
            var sb = new StringBuilder(length * 2);

            for (int k = 0; k < length; k++)
            {
                sb.AppendFormat("{0:X2}", bt[k]);
            }

            return sb.ToString();
        }

        public static byte[] ConvertHexStringToByteArray(string str)
        {
            var x = new byte[str.Length / 2];
            for (int k = 0; k < str.Length; k += 2)
            {
                try
                {
                    x[k / 2] = byte.Parse(str.Substring(k, 2), System.Globalization.NumberStyles.HexNumber);
                }
                catch {; }
            }
            return x;
        }

        public static string Convert7bitBinaryToString(string str)
        {
            int v;
            var sb = new StringBuilder();
            for (int i = 1; i <= str.Length / 7; i++)
            {
                string buff = str.Substring((i - 1) * 7, 7);
                v = 0;
                for (int j = 0; j < 7; j++)
                {
                    v = v + int.Parse(buff[j].ToString()) * (int)Math.Pow(2, (6 - j));
                }
                sb.Append((char)v);
            }
            return sb.ToString();
        }

        public static string Convert8bitBinaryToString(string str)
        {
            int v;
            var sb = new StringBuilder();
            for (int i = 1; i <= str.Length / 8; i++)
            {
                string buff = str.Substring((i - 1) * 8, 8);
                v = 0;
                for (int j = 0; j < 8; j++)
                {
                    v = v + int.Parse(buff[j].ToString()) * (int)Math.Pow(2, (7 - j));
                }
                sb.Append((char)v);
            }
            return sb.ToString();
        }

        /// <summary>
        /// converts ASCII string to binary (10100101)
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string StringToBinary(string str)
        {
            var sb = new StringBuilder();
            foreach (char c in str)
            {
                string bin = Convert.ToString(c, 2);
                while (bin.Length < 7)
                {
                    bin = "0" + bin;
                }
                sb.Append(bin);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts to binary string (01011101)
        /// and returns the string in 8 bit, makes SHIFTING
        /// </summary>
        /// <param name="str">String to convert</param>
        /// <returns>Converted string</returns>
        public static string Convert7bitTo8bit(string str)
        {
            string bin = StringToBinary(str);
            var sb = new StringBuilder();

            int span = 1;

            while (bin.Length >= 8)
            {
                bin = bin.Substring(15 - 2 * span, span) + bin;
                bin = bin.Remove(15 - span, span);
                if (bin.Length > 7)
                {
                    sb.Append(bin.Substring(0, 8));
                    bin = bin.Remove(0, 8);
                }
                else
                {
                    for (int k = 1; k <= 8 - bin.Length; k++)
                    {
                        sb.Append("0");
                    }
                    sb.Append(bin);
                    bin = "";
                }

                span++;
                if (span == 8)
                {
                    span = 1;
                }
            }

            if (bin != "")
            {
                for (int k = 1; k <= 8 - bin.Length; k++)
                {
                    sb.Append("0");
                }
                sb.Append(bin);
            }

            return Convert8bitBinaryToString(sb.ToString());
        }

        /// <summary>
        /// Convert 8 bit Hex string to 7 bit
        /// </summary>
        /// <param name="hex">Hex string</param>
        /// <returns>7 bit string</returns>
        public static string Convert8bitTo7bit(string hex)
        {
            var bin = new StringBuilder();
            var _8bit = new StringBuilder();

            for (int k = 0; k < hex.Length - 1; k += 2)
            {
                string buff = Convert.ToString(int.Parse(hex.Substring(k, 2), System.Globalization.NumberStyles.HexNumber), 2);
                for (int l = 1; l <= 8 - buff.Length; l++)
                {
                    bin.Append("0");
                }
                bin.Append(buff);
            }

            int span = 6;
            int hspan = 2;

            string head = bin.ToString()[0].ToString();
            _8bit.Append(bin.ToString().Substring(1, 7));

            for (int k = 2; k <= bin.Length / 8; k++)
            {
                if (hspan == 1)
                {
                    _8bit.Append(head + bin.ToString().Substring(k * 8 - span, span));
                }
                else
                {
                    _8bit.Append(bin.ToString().Substring(k * 8 - span, span) + head);
                }

                head = bin.ToString().Substring((k - 1) * 8, hspan);

                span--;
                if (span == 0)
                {
                    span = 7;
                }

                hspan++;
                if (hspan == 8)
                {
                    hspan = 1;
                }
            }
            _8bit.Append(bin.ToString().Substring(bin.Length / 8 * 8, bin.Length - bin.Length / 8 * 8) + head);

            return Convert7bitBinaryToString(_8bit.ToString());
        }
    }
}
