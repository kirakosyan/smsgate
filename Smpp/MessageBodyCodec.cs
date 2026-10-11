using System;
using System.Collections.Generic;
using System.Text;

namespace Smpp
{
    // Shared by submit_sm and deliver_sm so their length and UDH rules stay identical.
    internal static class MessageBodyCodec
    {
        internal readonly record struct Part(string Field, bool HasUserDataHeader);

        internal static Part[] Encode(string body, int coding, int largeMessageMethod,
            int asciiSplit, bool packed, byte reference)
        {
            ArgumentNullException.ThrowIfNull(body);
            if (asciiSplit < 1 || asciiSplit > Common.ASCII_MESSAGE_SPLIT_BASE)
                throw new ArgumentOutOfRangeException(nameof(asciiSplit));
            if (largeMessageMethod < 1 || largeMessageMethod > 4)
                throw new ArgumentOutOfRangeException(nameof(largeMessageMethod));

            bool hexBody = coding == 2 || coding == 245;
            if (coding == 245)
                body = EncodeWapPush(body);
            if (hexBody)
                body = Convert.ToHexString(Convert.FromHexString(body));

            int unit = hexBody ? 2 : 1;
            int limit = coding == 8 ? 70 : coding == 0 || coding == 1 ? 160 : 140;
            int split = coding == 8 ? Common.UNICODE_MESSAGE_SPLIT_BASE :
                coding == 0 || coding == 1 ? asciiSplit : 134;
            bool multipart = body.Length / unit > limit && largeMessageMethod == 1;
            if (body.Length / unit > limit && largeMessageMethod == 4)
                throw new ArgumentException("Message exceeds the single-message limit.", nameof(body));
            if (body.Length / unit > limit && largeMessageMethod == 3)
                body = body.Substring(0, SafeLength(body, 0, limit * unit, coding));

            var segments = new List<string>();
            if (multipart)
            {
                for (int offset = 0; offset < body.Length;)
                {
                    int length = SafeLength(body, offset, Math.Min(split * unit, body.Length - offset), coding);
                    segments.Add(body.Substring(offset, length));
                    offset += length;
                }
                if (segments.Count > byte.MaxValue)
                    throw new ArgumentException("A multipart message cannot exceed 255 segments.", nameof(body));
            }
            else
            {
                segments.Add(body);
            }

            var parts = new Part[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                string segment = segments[i];
                string hex;
                if (hexBody)
                    hex = segment;
                else if (coding == 8)
                    hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(segment));
                else if (packed && (coding == 0 || coding == 1))
                    hex = Pack(segment, multipart ? 1 : 0);
                else
                    hex = Common.StringToHex(segment);

                if (multipart)
                    hex = $"050003{reference:X2}{segments.Count:X2}{i + 1:X2}" + hex;

                int length = hex.Length / 2;
                bool payload = largeMessageMethod == 2;
                if (length > (payload ? ushort.MaxValue : 254))
                    throw new ArgumentException("Encoded message exceeds the SMPP field length.", nameof(body));
                string field = payload ? $"000424{length:X4}{hex}" : $"{length:X2}{hex}";
                parts[i] = new Part(field, multipart || coding == 245);
            }
            return parts;
        }

        private static int SafeLength(string body, int offset, int length, int coding)
        {
            if (coding == 8 && offset + length < body.Length &&
                char.IsHighSurrogate(body[offset + length - 1]) && char.IsLowSurrogate(body[offset + length]))
                length--;
            return length;
        }

        internal static string Decode(string hex, int coding, int esmClass, bool packed, out string multipartHeader)
        {
            multipartHeader = "";
            // A body that merely starts with these bytes is not a UDH unless UDHI is set.
            if ((esmClass & 0x40) != 0 && hex.StartsWith("050003", StringComparison.OrdinalIgnoreCase))
            {
                if (hex.Length < 12)
                    throw new FormatException("Truncated concatenation header.");
                multipartHeader = hex.Substring(0, 12);
                hex = hex.Substring(12);
            }
            if (coding == 2 || coding == 245)
                return hex.ToUpperInvariant();
            if (coding == 8)
                return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));
            if (packed && (coding == 0 || coding == 1))
                return Unpack(hex, multipartHeader.Length > 0 ? 1 : 0);
            return coding == 0 || coding == 1 || coding == 3 ? Common.HexToString(hex) : hex.ToUpperInvariant();
        }

        private static string Pack(string body, int fillBits)
        {
            var bytes = new byte[(body.Length * 7 + fillBits + 7) / 8];
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] > 127)
                    throw new ArgumentException("Packed ASCII requires 7-bit characters.", nameof(body));
                int bit = fillBits + i * 7;
                bytes[bit / 8] |= (byte)(body[i] << (bit % 8));
                if (bit % 8 > 1)
                    bytes[bit / 8 + 1] |= (byte)(body[i] >> (8 - bit % 8));
            }
            return Convert.ToHexString(bytes);
        }

        private static string Unpack(string hex, int fillBits)
        {
            var bytes = Convert.FromHexString(hex);
            var result = new StringBuilder();
            for (int bit = fillBits; bit + 7 <= bytes.Length * 8; bit += 7)
            {
                int value = bytes[bit / 8] >> (bit % 8);
                if (bit % 8 > 1)
                    value |= bytes[bit / 8 + 1] << (8 - bit % 8);
                result.Append((char)(value & 127));
            }
            // SMPP carries the octet count, so a final zero septet may be padding.
            return result.ToString().TrimEnd('\0');
        }

        internal static string EncodeWapPush(string body)
        {
            if (body.StartsWith("0605", StringComparison.OrdinalIgnoreCase))
                return body;
            string token = "0C";
            string rest = body;
            foreach (var entry in new[] { ("http://www.", "0D"), ("https://www.", "0F"), ("http://", "0C"), ("https://", "0E") })
            {
                if (body.StartsWith(entry.Item1, StringComparison.Ordinal))
                {
                    token = entry.Item2;
                    rest = body.Substring(entry.Item1.Length);
                    break;
                }
            }
            return $"0605040B8423F05F0601AE02056A0045C6{token}03{Common.StringToHex(rest)}00070103000101";
        }
    }
}
