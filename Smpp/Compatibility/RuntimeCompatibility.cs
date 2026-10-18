using System;
using System.Globalization;
using System.Diagnostics;
using System.Text;

namespace Smpp
{
    internal static class RuntimeCompatibility
    {
        internal static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        internal static TimeSpan GetElapsedTime(long startingTimestamp)
        {
#if NETFRAMEWORK
            return StopwatchTicksToTimeSpan(Stopwatch.GetTimestamp() - startingTimestamp);
#else
            return Stopwatch.GetElapsedTime(startingTimestamp);
#endif
        }
        internal static TimeSpan StopwatchTicksToTimeSpan(long ticks) =>
            TimeSpan.FromTicks((long)(ticks * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
#if NETFRAMEWORK
        private static readonly Random Random = new Random();
#endif
        internal static int NextMultipartReference()
        {
#if NETFRAMEWORK
            lock (Random) return Random.Next(1, 256);
#else
            return Random.Shared.Next(1, 256);
#endif
        }
        internal static bool TryParseLength(string text, int offset, out uint length)
        {
#if NETFRAMEWORK
            return uint.TryParse(text.Substring(offset, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out length);
#else
            return uint.TryParse(text.AsSpan(offset, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out length);
#endif
        }
    }
}
