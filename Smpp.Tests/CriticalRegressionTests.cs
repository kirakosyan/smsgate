using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;

namespace Smpp.Tests
{
    [TestFixture]
    public class CriticalRegressionTests
    {
        [TearDown]
        public void TearDown()
        {
            try { Server.Stop(); } catch { }
            Gate.Clients.Clear();
            Gate.Servers.Clear();
        }

        [Test]
        public void AddServerConnection_ShouldRejectDuplicateSystemId()
        {
            var gate = new Gate(new GateEvents());
            gate.AddServerConnection("server-1", "system", "password-1");

            Assert.Throws<ArgumentException>(() =>
                gate.AddServerConnection("server-2", "system", "password-2"));
        }

        [Test]
        public void ServerBind_ShouldReadFragmentedBindFrameExactly()
        {
            var port = GetFreePort();
            var gate = new Gate(new GateEvents());
            gate.AddServerConnection("server", "system", "password");
            Server.StartAsync("127.0.0.1", port);
            WaitForServerStart();

            var bind = new BindTransceiver
            {
                SystemID = "system",
                Password = "password",
                InterfaceVersion = Common.SmppVersionType.Version3_4,
                sequence_number = 77
            };
            var bindBytes = Common.ConvertHexStringToByteArray(bind.Encode());

            using (var tcpClient = new TcpClient())
            {
                tcpClient.Connect(IPAddress.Loopback, port);
                tcpClient.ReceiveTimeout = 3000;
                var stream = tcpClient.GetStream();

                stream.Write(bindBytes, 0, 10);
                Thread.Sleep(200);
                stream.Write(bindBytes, 10, bindBytes.Length - 10);

                var response = ReadPdu(stream);
                var bindResponse = new BindTransceiverResp(Common.ConvertByteArrayToHexString(response));

                Assert.That(bindResponse.command_status, Is.EqualTo(0));
                Assert.That(bindResponse.sequence_number, Is.EqualTo(77));
            }
        }

        [Test]
        public void ServerBind_ShouldCloseConnectionForInvalidAdvertisedLength()
        {
            var port = GetFreePort();
            var gate = new Gate(new GateEvents());
            gate.AddServerConnection("server", "system", "password");
            Server.StartAsync("127.0.0.1", port);
            WaitForServerStart();

            using (var tcpClient = new TcpClient())
            {
                tcpClient.Connect(IPAddress.Loopback, port);
                tcpClient.ReceiveTimeout = 3000;
                var stream = tcpClient.GetStream();
                stream.Write(new byte[] { 0x00, 0x00, 0x07, 0xD0 }, 0, 4);

                var buffer = new byte[1];
                Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(0));
            }
        }

        [Test]
        public void ServerBind_ShouldCloseConnectionForUnknownCommandId()
        {
            var port = GetFreePort();
            var gate = new Gate(new GateEvents());
            gate.AddServerConnection("server", "system", "password");
            Server.StartAsync("127.0.0.1", port);
            WaitForServerStart();

            using (var tcpClient = new TcpClient())
            {
                tcpClient.Connect(IPAddress.Loopback, port);
                tcpClient.ReceiveTimeout = 3000;
                var stream = tcpClient.GetStream();
                var unknownCommand = Common.ConvertHexStringToByteArray("00000010FFFFFFFF0000000000000001");
                stream.Write(unknownCommand, 0, unknownCommand.Length);

                var buffer = new byte[1];
                Assert.That(stream.Read(buffer, 0, 1), Is.EqualTo(0));
            }
        }

        [Test]
        public void MessageHandler_ShouldPreserveFragmentedIncomingPdu()
        {
            var receivedMessages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (channel, messageId, sender, recipient, body, bodyFormat, registeredDelivery) =>
                receivedMessages.Add(body);

            var gate = new Gate(events);
            var client = gate.AddClientConnection("client", "127.0.0.1", 2775, "system", "password");
            var deliverSm = new DeliverSm(events)
            {
                Sender = "1000",
                Recipient = "2000",
                Body = "fragmented message",
                BodyFormat = "ascii",
                RegisteredDelivery = 0,
                sequence_number = 5
            };

            var pdu = deliverSm.EncodeM()[0];
            client.MessageHandler(pdu.Substring(0, 18));
            Assert.That(receivedMessages, Is.Empty);

            client.MessageHandler(pdu.Substring(18));
            Assert.That(receivedMessages, Does.Contain("fragmented message"));
        }

        [Test]
        public void SubmitSm_ShouldTrackEveryMultipartSequence()
        {
            var port = GetFreePort();
            using (var serverReady = new ManualResetEventSlim(false))
            using (var bindReceived = new ManualResetEventSlim(false))
            {
                var serverTask = Task.Run(() => RunBindOnlyServer(port, serverReady, bindReceived));
                Assert.That(serverReady.Wait(3000), Is.True);

                var gate = new Gate(new GateEvents());
                var client = gate.AddClientConnection("client", "127.0.0.1", port, "system", "password");
                Assert.That(client.Connect(), Is.True);
                Assert.That(bindReceived.Wait(3000), Is.True);
                Assert.That(SpinWait.SpinUntil(() => client.connected, 3000), Is.True);

                client.large_message = 1;
                client.SubmitSm(42, "1000", "2000", new string('a', 170));

                var submittedMessages = GetSubmittedMessageCount(client);
                Assert.That(submittedMessages, Is.EqualTo(2));

                client.Quit();
                Assert.That(serverTask.Wait(3000), Is.True);
            }
        }

        private static int GetSubmittedMessageCount(Client client)
        {
            var field = typeof(Connection).GetField("submitted_messages", BindingFlags.Instance | BindingFlags.NonPublic);
            var value = field.GetValue(client);
            var countProperty = value.GetType().GetProperty("Count");
            return (int)countProperty.GetValue(value);
        }

        private static void RunBindOnlyServer(int port, ManualResetEventSlim serverReady, ManualResetEventSlim bindReceived)
        {
            using (var listener = new TcpListener(IPAddress.Loopback, port))
            {
                listener.Start();
                serverReady.Set();
                using (var tcpClient = listener.AcceptTcpClient())
                {
                    tcpClient.ReceiveTimeout = 3000;
                    using (var stream = tcpClient.GetStream())
                    {
                        var bind = ReadPdu(stream);
                        var bindHex = Common.ConvertByteArrayToHexString(bind);
                        var bindResponse = new BindTransceiverResp
                        {
                            command_status = 0,
                            sequence_number = uint.Parse(bindHex.Substring(24, 8), System.Globalization.NumberStyles.HexNumber)
                        };
                        var response = Common.ConvertHexStringToByteArray(bindResponse.Encode());
                        stream.Write(response, 0, response.Length);
                        bindReceived.Set();

                        ReadPdu(stream);
                        ReadPdu(stream);
                        // Keep the transport alive until the caller finishes inspecting pending ACKs.
                        // Quit sends the third PDU (unbind); do not race EOF against the assertion.
                        ReadPdu(stream);
                    }
                }
            }
        }

        private static byte[] ReadPdu(NetworkStream stream)
        {
            var header = new byte[4];
            ReadExact(stream, header, 0, header.Length);
            var length = int.Parse(Common.ConvertByteArrayToHexString(header), System.Globalization.NumberStyles.HexNumber);
            var pdu = new byte[length];
            Buffer.BlockCopy(header, 0, pdu, 0, header.Length);
            ReadExact(stream, pdu, header.Length, length - header.Length);
            return pdu;
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                var read = stream.Read(buffer, offset, count);
                if (read == 0)
                {
                    throw new InvalidOperationException("Socket closed before full PDU was read.");
                }

                offset += read;
                count -= read;
            }
        }

        private static int GetFreePort()
        {
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
        }

        private static void WaitForServerStart()
        {
            SpinWait.SpinUntil(() => Server.running, TimeSpan.FromSeconds(3));
        }
    }
}
