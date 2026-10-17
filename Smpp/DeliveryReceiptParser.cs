using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Smpp
{
    internal static class DeliveryReceiptParser
    {
        internal static DeliveryReceipt Parse(string channel, string body, SortedList<string, object> parameters)
        {
            string id = TextField(body, "id");
            string stateText = TextField(body, "stat").ToUpperInvariant();
            string error = TextField(body, "err");
            byte state = stateText switch
            {
                "ENROUTE" or "ENROUT" => 1, "DELIVRD" or "DELIVR" => 2,
                "EXPIRED" or "EXPIRE" => 3, "DELETED" or "DELETE" => 4,
                "UNDELIV" or "UNDELI" => 5, "ACCEPTD" or "ACCEPT" => 6,
                "UNKNOWN" or "UNKNOW" => 7, "REJECTD" or "REJECT" => 8, _ => 0
            };
            if (parameters.TryGetValue("001E", out var receiptId))
            {
                byte[] bytes = Convert.FromHexString(receiptId.ToString());
                if (bytes.Length < 2 || bytes.Length > 65 || bytes[^1] != 0 || Array.IndexOf(bytes, (byte)0) != bytes.Length - 1)
                    throw new FormatException("Invalid receipted_message_id TLV.");
                id = Common.HexToString(Convert.ToHexString(bytes.AsSpan(0, bytes.Length - 1)));
            }
            if (parameters.TryGetValue("0427", out var receiptState))
            {
                byte[] bytes = Convert.FromHexString(receiptState.ToString());
                if (bytes.Length != 1)
                    throw new FormatException("Invalid message_state TLV.");
                state = bytes[0];
            }
            string networkError = "";
            if (parameters.TryGetValue("0423", out var network))
            {
                byte[] bytes = Convert.FromHexString(network.ToString());
                if (bytes.Length != 3)
                    throw new FormatException("Invalid network_error_code TLV.");
                networkError = Convert.ToHexString(bytes);
            }
            if (string.IsNullOrWhiteSpace(id) || state < 1 || state > 8)
                return null;
            return new DeliveryReceipt(channel, id, ToStatus(state), state, error, networkError);
        }

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
