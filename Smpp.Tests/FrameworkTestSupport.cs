#if NETFRAMEWORK
global using SslClientAuthenticationOptions = Smpp.SmppTlsClientOptions;
global using SslServerAuthenticationOptions = Smpp.SmppTlsServerOptions;
global using TaskCompletionSource = Smpp.Tests.CompletionSource;
global using Convert = Smpp.Tests.FrameworkConvert;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Smpp.Tests
{
    internal sealed class CompletionSource : TaskCompletionSource<bool>
    {
        internal CompletionSource(TaskCreationOptions options = TaskCreationOptions.None) : base(options) { }
        internal void SetResult() => SetResult(true);
        internal bool TrySetResult() => TrySetResult(true);
    }

    // Independent wire conversion for the shared regression tests.
    internal static class FrameworkConvert
    {
        internal static uint ToUInt32(string value, int radix) => System.Convert.ToUInt32(value, radix);
        internal static string ToHexString(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
        internal static byte[] FromHexString(string hex) => Enumerable.Range(0, hex.Length / 2)
            .Select(index => System.Convert.ToByte(hex.Substring(index * 2, 2), 16)).ToArray();
    }

    internal static class FrameworkTestStreams
    {
        internal static void ReadExactly(this Stream stream, Span<byte> destination)
        {
            var bytes = new byte[destination.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            bytes.AsSpan().CopyTo(destination);
        }
    }
}
#endif
