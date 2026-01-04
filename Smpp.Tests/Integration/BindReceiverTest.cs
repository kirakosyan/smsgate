using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Smpp.Events;

namespace Smpp.Tests.Integration
{
    [TestFixture]
    public class BindReceiverTest
    {
        private readonly object _logLock = new object();
        List<string> logBuffer = new List<string>();

        [Test]
        public void TestBindReceiverConnection()
        {
            var gate = new Gate(new GateEvents());

            var server = gate.AddServerConnection("TestSrver", "sysId", "sysPass");
            server.use_deliver_sm = false;

            Server.StartAsync("127.0.0.1", 2775);

            gate.Events.ChannelEvent += Events_ChannelEvent;

            var client = gate.AddClientConnection("bg", "127.0.0.1", 2775, "sysId", "sysPass");

            // this will make it only receiver and will connect as bind_receiver
            client.direction_in = true;
            client.direction_out = false;

            // turn ON debug to show PDU
            // client.debug = true;

            // make timeout longer for debug
            client.timeout = 30000;

            var c = client.Connect();

            Thread.Sleep(1000);

            List<string> logSnapshot;
            lock (_logLock)
            {
                logSnapshot = new List<string>(logBuffer);
            }

            Assert.That(c, Is.True, "Connection should be established");

            Assert.That(logSnapshot, Does.Contain("Sending [bind_receiver]"), "Log buffer should contain bind receiver");
            Assert.That(logSnapshot, Does.Contain("Receiving [bind_receiver_resp]"), "Log buffer should contain bind receiver response");

            Thread.Sleep(1000);
            client.Quit();
            Server.Stop();
        }

        void Events_ChannelEvent(string channelName, string description, string pdu)
        {
            Debug.WriteLine(DateTime.Now + ": " + channelName + ": " + description + ". PDU: " + pdu);
            lock (_logLock)
            {
                logBuffer.Add(description);
            }
        }
    }
}
