using System;
using System.Collections.Generic;
using System.Text;

namespace Smpp
{
    /// <summary>The GSM 03.38 default alphabet and extension table, before optional septet packing.</summary>
    public static class Gsm7Encoding
    {
        private const string Alphabet =
            "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ\u001bÆæßÉ" +
            " !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

        private static readonly Dictionary<char, byte> Extensions = new Dictionary<char, byte>
        {
            ['\f'] = 0x0A, ['^'] = 0x14, ['{'] = 0x28, ['}'] = 0x29, ['\\'] = 0x2F,
            ['['] = 0x3C, ['~'] = 0x3D, [']'] = 0x3E, ['|'] = 0x40, ['€'] = 0x65
        };

        /// <summary>Returns whether every character can be represented without replacement.</summary>
        public static bool CanEncode(string text)
        {
            Guard.ThrowIfNull(text);
            foreach (char value in text)
                if (value == '\u001b' || (Alphabet.IndexOf(value) < 0 && !Extensions.ContainsKey(value)))
                    return false;
            return true;
        }

        /// <summary>Counts septets, including the escape septet for extension characters.</summary>
        public static int GetSeptetCount(string text) => Encode(text).Length;

        /// <summary>Encodes unpacked septets. Unsupported characters are rejected, never replaced.</summary>
        public static byte[] Encode(string text)
        {
            Guard.ThrowIfNull(text);
            var result = new List<byte>(text.Length);
            foreach (char value in text)
            {
                if (Extensions.TryGetValue(value, out byte extension))
                {
                    result.Add(0x1B);
                    result.Add(extension);
                }
                else
                {
                    int index = Alphabet.IndexOf(value);
                    if (index < 0 || index == 0x1B)
                        throw new ArgumentException("Text contains a character outside the GSM-7 alphabet. Use unicode or auto.", nameof(text));
                    result.Add((byte)index);
                }
            }
            return result.ToArray();
        }

        /// <summary>Decodes unpacked septets. Unknown extensions fall back to the base alphabet; strict mode rejects them.</summary>
        public static string Decode(ReadOnlySpan<byte> septets, bool strict = false)
        {
            var result = new StringBuilder(septets.Length);
            for (int index = 0; index < septets.Length; index++)
            {
                byte value = septets[index];
                if (value > 0x7F)
                    throw new FormatException("A GSM-7 septet exceeds seven bits.");
                if (value != 0x1B)
                {
                    result.Append(Alphabet[value]);
                    continue;
                }
                if (++index == septets.Length)
                {
                    if (strict) throw new FormatException("Truncated GSM-7 extension escape.");
                    result.Append(' ');
                    break;
                }
                bool found = false;
                foreach (var extension in Extensions)
                    if (extension.Value == septets[index])
                    {
                        result.Append(extension.Key);
                        found = true;
                        break;
                    }
                if (!found)
                {
                    if (strict || septets[index] > 127) throw new FormatException("Unknown GSM-7 extension character.");
                    result.Append(septets[index] == 0x1B ? ' ' : Alphabet[septets[index]]);
                }
            }
            return result.ToString();
        }
    }
}
