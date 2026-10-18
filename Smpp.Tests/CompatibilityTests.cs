using System;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class CompatibilityTests
    {
        [Test]
        public void EveryOctetAndMixedCaseHexRoundTrips()
        {
            var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            string expected = BitConverter.ToString(bytes).Replace("-", "");
            Assert.That(HexCodec.ToHexString(bytes), Is.EqualTo(expected));
            Assert.That(HexCodec.FromHexString(expected.ToLowerInvariant()), Is.EqualTo(bytes));
            Assert.That(HexCodec.FromHexString(""), Is.Empty);
            Assert.That(HexCodec.ToHexString(Array.Empty<byte>()), Is.Empty);
        }

        [TestCase("F")]
        [TestCase("FG")]
        [TestCase(" F")]
        [TestCase("0xFF")]
        [TestCase("ＦＦ")]
        public void InvalidHexIsRejected(string value) => Assert.Throws<FormatException>(() => HexCodec.FromHexString(value));

        [Test]
        public void GuardPreservesArgumentNames()
        {
            object missing = null;
            Assert.That(Assert.Throws<ArgumentNullException>(() => Guard.ThrowIfNull(missing)).ParamName, Is.EqualTo("missing"));
            int negative = -1;
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => Guard.ThrowIfNegative(negative)).ParamName, Is.EqualTo("negative"));
        }

        [Test]
        public void PortableTlsDefaultsUseSystemProtocolsAndCertificateValidation()
        {
            var portable = new SmppTlsClientOptions { TargetHost = "localhost" };
            var options = new SmppClientOptions { TlsOptions = portable };
            Assert.That(options.TlsOptions.TargetHost, Is.EqualTo("localhost"));
            Assert.That(portable.EnabledSslProtocols, Is.EqualTo(SslProtocols.None));
            Assert.That(portable.CheckCertificateRevocation, Is.True);
            Assert.That(portable.CertificateValidationCallback, Is.Null);
#if !NETFRAMEWORK
            SslClientAuthenticationOptions native = portable;
            Assert.That(native.CertificateRevocationCheckMode, Is.EqualTo(System.Security.Cryptography.X509Certificates.X509RevocationMode.Online));
            Assert.That(native.RemoteCertificateValidationCallback, Is.Null);
            SslServerAuthenticationOptions server = new SmppTlsServerOptions { ClientCertificateRequired = true };
            Assert.That(server.ClientCertificateRequired, Is.True);
#endif
        }

        [Test]
        public async Task DisposedListenerCancellationStillClosesTheAcceptedSocket()
        {
            using var pair = new SocketPair();
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            cancellation.Cancel();
            cancellation.Dispose();
            await Server.CreateConnection(null, pair.Local, token).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(await pair.Remote.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)), Is.Zero);
        }

#if NETFRAMEWORK
        private sealed class OwnedResource : IDisposable
        {
            internal bool Disposed;
            public void Dispose() => Disposed = true;
        }

        [Test]
        public void CancellationAtAcceptCompletionDisposesTheUnclaimedResource()
        {
            using var cancellation = new CancellationTokenSource();
            var resource = new OwnedResource();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await FrameworkAsync.WithCancellation(() =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(resource);
                }, () => { }, cancellation.Token));
            Assert.That(resource.Disposed, Is.True);
        }

        [Test]
        public async Task CancellingOrTimingOutAWaitDoesNotCancelTheUnderlyingTask()
        {
            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            var waiting = completion.Task.WaitAsync(cancellation.Token);
            cancellation.Cancel();
            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.ThrowsAsync<TimeoutException>(async () => await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(30)));
            completion.SetResult(42);
            Assert.That(await completion.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo(42));
        }

        [Test]
        public async Task CancellingAnExactReadClosesAPendingFrameworkSocketRead()
        {
            using var pair = new SocketPair();
            using var cancellation = new CancellationTokenSource();
            var reading = pair.Local.GetStream().ReadExactlyAsync(new byte[16], cancellation.Token).AsTask();
            cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await reading.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.That(await pair.Remote.GetStream().ReadAsync(new byte[1]), Is.Zero);
        }

        [Test]
        public void ReportActualFrameworkRuntime()
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            int release = (int)key.GetValue("Release");
            TestContext.Out.WriteLine($"Actual Framework Release={release}, Version={key.GetValue("Version")}, CLR={Environment.Version}, x64={Environment.Is64BitProcess}");
            Assert.That(release, Is.GreaterThanOrEqualTo(461808));
        }
#endif
    }
}
