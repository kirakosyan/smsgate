using System;

namespace Smpp
{
    internal static class HexCodec
    {
        internal static string ToHexString(ReadOnlySpan<byte> bytes)
        {
#if NETFRAMEWORK
            const string digits = "0123456789ABCDEF";
            var chars = new char[checked(bytes.Length * 2)];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 15];
            }
            return new string(chars);
#else
            return Convert.ToHexString(bytes);
#endif
        }
        internal static byte[] FromHexString(string text)
        {
#if NETFRAMEWORK
            Guard.ThrowIfNull(text);
            if ((text.Length & 1) != 0) throw new FormatException("Hexadecimal text must contain complete octets.");
            var bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = Digit(text[i * 2]), low = Digit(text[i * 2 + 1]);
                if (high < 0 || low < 0) throw new FormatException("Invalid hexadecimal digit.");
                bytes[i] = (byte)((high << 4) | low);
            }
            return bytes;
#else
            return Convert.FromHexString(text);
#endif
        }
#if NETFRAMEWORK
        private static int Digit(char value) => value >= '0' && value <= '9' ? value - '0' :
            value >= 'A' && value <= 'F' ? value - 'A' + 10 : value >= 'a' && value <= 'f' ? value - 'a' + 10 : -1;
#endif
    }
}
