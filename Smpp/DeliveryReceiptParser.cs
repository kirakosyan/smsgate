using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Smpp
{
    internal static class DeliveryReceiptParser
    {
        internal static DeliveryReceipt Parse(string channel, string body, SortedList<string, object> parameters, Action<string> warning = null)
        {
            string id = TextField(body, "id");
            string stateText = TextField(body, "stat").ToUpperInvariant();
            string error = TextField(body, "err");
            byte state = ParseState(stateText);
            if (parameters.TryGetValue("001E", out var receiptId))
            {
                byte[] bytes = HexCodec.FromHexString(receiptId.ToString());
                if (bytes.Length < 2 || bytes.Length > 65 || bytes[bytes.Length - 1] != 0 || Array.IndexOf(bytes, (byte)0) != bytes.Length - 1)
                    warning?.Invoke("Ignoring invalid receipted_message_id TLV; using receipt text when available.");
                else
                    id = Common.HexToString(HexCodec.ToHexString(bytes.AsSpan(0, bytes.Length - 1)));
            }
            if (parameters.TryGetValue("0427", out var receiptState))
            {
                byte[] bytes = HexCodec.FromHexString(receiptState.ToString());
                if (bytes.Length != 1 || bytes[0] < 1 || bytes[0] > 8)
                    warning?.Invoke("Ignoring invalid message_state TLV; using receipt text when available.");
                else
                    state = bytes[0];
            }
            string networkError = "";
            if (parameters.TryGetValue("0423", out var network))
            {
                byte[] bytes = HexCodec.FromHexString(network.ToString());
                if (bytes.Length != 3)
                    warning?.Invoke("Ignoring invalid network_error_code TLV; using receipt text when available.");
                else
                    networkError = HexCodec.ToHexString(bytes);
            }
            if (string.IsNullOrWhiteSpace(id) || state < 1 || state > 8)
                return null;
            return new DeliveryReceipt(channel, id, ToStatus(state), state, error, networkError);
        }

        internal static byte ParseState(string stateText) => stateText switch
            {
                "ENROUTE" or "ENROUT" => 1, "DELIVRD" or "DELIVR" => 2,
                "EXPIRED" or "EXPIRE" => 3, "DELETED" or "DELETE" => 4,
                "UNDELIV" or "UNDELI" => 5, "ACCEPTD" or "ACCEPT" => 6,
                "UNKNOWN" or "UNKNOW" => 7, "REJECTD" or "REJECT" => 8, _ => 0
            };

        internal static string StateText(byte state, bool legacy = false)
        {
            string text = state switch
            {
                1 => "ENROUTE", 2 => "DELIVRD", 3 => "EXPIRED", 4 => "DELETED",
                5 => "UNDELIV", 6 => "ACCEPTD", 7 => "UNKNOWN", 8 => "REJECTD", _ => ""
            };
            return legacy && text.Length > 6 ? text.Substring(0, 6) : text;
        }
        internal static byte FromStatus(Common.MessageStatus status) => status switch
        {
            Common.MessageStatus.delivered_ACK_received or Common.MessageStatus.duplicate_ACK_detected => 2,
            Common.MessageStatus.ACK_expired or Common.MessageStatus.timeout => 3,
            Common.MessageStatus.queued or Common.MessageStatus.received_waiting_to_be_processed or
                Common.MessageStatus.scheduled or Common.MessageStatus.received_routed => 6,
            Common.MessageStatus.sent or Common.MessageStatus.submitted_waiting_for_ACK => 1,
            Common.MessageStatus.generic_error or Common.MessageStatus.locked_by_system or
                Common.MessageStatus.NACK_received or Common.MessageStatus.rejected or Common.MessageStatus.out_of_balance => 8,
            Common.MessageStatus.message_undeliverable or Common.MessageStatus.received_route_failure or
                Common.MessageStatus.received_no_route_specified or Common.MessageStatus.no_channel_can_handle_message => 5,
            _ => 7
        };

        internal static Common.MessageStatus ToStatus(byte state) => state switch
        {
            1 => Common.MessageStatus.submitted_waiting_for_ACK,
            2 => Common.MessageStatus.delivered_ACK_received,
            3 => Common.MessageStatus.ACK_expired,
            4 or 5 => Common.MessageStatus.message_undeliverable,
            6 => Common.MessageStatus.received_routed,
            7 => Common.MessageStatus.unknown_recipient,
            8 => Common.MessageStatus.rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        private static string TextField(string body, string key) =>
            Regex.Match(body ?? "", @"(?:^|\s)" + key + @":\s*([^\s:]+)(?=\s|$)", RegexOptions.IgnoreCase).Groups[1].Value;
    }
}
