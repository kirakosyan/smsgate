using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture, NonParallelizable]
    public class ServerSessionRegressionTests
    {
        internal sealed class ServerHost : IDisposable
        {
            internal readonly Server Server;
            internal readonly int Port;
            internal readonly string Channel = "review-server-" + Guid.NewGuid();
            internal readonly string Login = Guid.NewGuid().ToString("n").Substring(0, 8);
            internal ServerHost(GateEvents events = null)
            {
                Server = new Gate(events ?? new GateEvents()).AddServerConnection(Channel, Login, "password");
                Smpp.Server.StartAsync("127.0.0.1", 0);
                var listener = (TcpListener)typeof(Server).GetField("tcp_server", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            internal TcpClient Bind(int mode, uint expectedStatus = 0, string password = "password")
            {
                var peer = new TcpClient("127.0.0.1", Port);
                string body = Common.StringToHex(Login) + "00" + Common.StringToHex(password) + "000034000000";
                WritePdu(peer.GetStream(), $"{16 + body.Length / 2:X8}{mode:X8}000000000000002A{body}");
                string response = ReadPdu(peer.GetStream());
                Assert.That(Convert.ToUInt32(response.Substring(8, 8), 16), Is.EqualTo((uint)mode | 0x80000000));
                Assert.That(Convert.ToUInt32(response.Substring(16, 8), 16), Is.EqualTo(expectedStatus));
                Assert.That(Convert.ToUInt32(response.Substring(24, 8), 16), Is.EqualTo(42));
                if (expectedStatus == 0)
                    WaitUntil(() => Server.connected, "Server should complete the bind");
                return peer;
            }
            public void Dispose() => Smpp.Server.Stop();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(9)]
        public void AuthenticationFailureReturnsAnErrorAndClosesOnlyThatSocket(int mode)
        {
            using var host = new ServerHost();
            using var invalid = host.Bind(mode, 13, "wrong");
            Assert.That(invalid.GetStream().ReadByte(), Is.EqualTo(-1));
            Assert.That(host.Server.connected, Is.False);
            using var valid = host.Bind(mode);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(9)]
        public void RemoteUnbindPreservesCredentialsAndAllowsRepeatedRebinds(int mode)
        {
            using var host = new ServerHost();
            for (int i = 0; i < 3; i++)
            {
                using var peer = host.Bind(mode);
                WritePdu(peer.GetStream(), "0000001000000006000000000000002B");
                Assert.That(ReadPdu(peer.GetStream()), Is.EqualTo("0000001080000006000000000000002B"));
                WaitUntil(() => !host.Server.connected, "Unbind should close the session");
                lock (Gate.Servers)
                    Assert.That(Gate.Servers.ContainsKey(host.Channel), Is.True, "Credentials must outlive the socket");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EofAndResetPreserveCredentialsAndAllowRebind(bool reset)
        {
            using var host = new ServerHost();
            using (var first = host.Bind(9))
            {
                if (reset) first.Client.LingerState = new LingerOption(true, 0);
                first.Dispose();
                WaitUntil(() => !host.Server.connected, "Lost transport must clear bound state");
            }
            using var second = host.Bind(9);
            WritePdu(second.GetStream(), "0000001000000015000000000000002C");
            Assert.That(ReadPdu(second.GetStream()), Is.EqualTo("0000001080000015000000000000002C"));
        }

        [Test]
        public void KeepaliveTimeoutPreservesCredentialsAndAllowsRebind()
        {
            using var host = new ServerHost();
            host.Server.timeout = 60;
            host.Server.keep_alive = 30;
            using var first = host.Bind(9);
            Assert.That(ReadPdu(first.GetStream()).Substring(8, 8), Is.EqualTo("00000015"));
            WaitUntil(() => !host.Server.connected, "Unanswered enquire_link must expire");
            host.Server.timeout = 3000;
            host.Server.keep_alive = 60000;
            using var second = host.Bind(9);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ServerOriginatedMessageTimeoutPreservesRegistration(bool deliver)
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var host = new ServerHost(events);
            host.Server.use_deliver_sm = deliver;
            host.Server.timeout = 50;
            using var first = host.Bind(1);
            Assert.That(host.Server.SubmitSm(42, "1", "2", "message"), Is.Zero);
            ReadPdu(first.GetStream());
            WaitUntil(() => statuses.Contains(Common.MessageStatus.timeout), "Unacknowledged server message must expire");
            host.Server.timeout = 3000;
            Assert.That(host.Server.connected, Is.True);
            WritePdu(first.GetStream(), "0000001000000015000000000000002C");
            Assert.That(ReadPdu(first.GetStream()), Is.EqualTo("0000001080000015000000000000002C"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ServerMessageAcknowledgementsEmitSentWithTheRemoteId(bool deliver)
        {
            var statuses = new ConcurrentQueue<(Common.MessageStatus, string)>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, id) => statuses.Enqueue((status, id));
            using var host = new ServerHost(events);
            host.Server.use_deliver_sm = deliver;
            using var peer = host.Bind(1);
            Assert.That(host.Server.SubmitSm(42, "1", "2", "æøå", "latin"), Is.Zero);
            string frame = ReadPdu(peer.GetStream());
            Assert.That(Decode(deliver ? "deliver" : "data", frame).Body, Is.EqualTo("æøå"));
            uint sequence = Convert.ToUInt32(frame.Substring(24, 8), 16);
            uint command = Convert.ToUInt32(frame.Substring(8, 8), 16) | 0x80000000;
            string id = Common.StringToHex("remote42") + "00";
            WritePdu(peer.GetStream(), $"{16 + id.Length / 2:X8}{command:X8}00000000{sequence:X8}{id}");
            WaitUntil(() => statuses.Contains((Common.MessageStatus.sent, "remote42")), "Successful ACK must report sent");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(9)]
        public void ASecondBindCannotReplaceAnActiveSession(int mode)
        {
            using var host = new ServerHost();
            using var first = host.Bind(mode);
            using var duplicate = host.Bind(mode, 5);
            Assert.That(duplicate.GetStream().ReadByte(), Is.EqualTo(-1));
            WritePdu(first.GetStream(), "0000001000000015000000000000002C");
            Assert.That(ReadPdu(first.GetStream()), Is.EqualTo("0000001080000015000000000000002C"));
        }

        [Test]
        public void ListenerIsReadyOnReturnAndRepeatedStartIsRejected()
        {
            using var host = new ServerHost();
            Assert.Throws<InvalidOperationException>(() => Server.StartAsync("127.0.0.1", 0));
            using var peer = host.Bind(9);
        }

        [Test]
        public void ExplicitQuitStillUnregistersServerCredentials()
        {
            using var host = new ServerHost();
            host.Server.Quit();
            lock (Gate.Servers)
                Assert.That(Gate.Servers.ContainsKey(host.Channel), Is.False);
        }
    }
}
