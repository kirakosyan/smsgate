using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Smpp.Events;

namespace Smpp.Tests.Integration
{
    /// <summary>
    /// Test creates 2 server and 2 client channels, as a result there will be 2 parallel connections
    /// </summary>
    [TestFixture]
    public class SmokeTest
    {
        List<string> logBuffer = new List<string>();
        List<string> messages = new List<string>();
        List<string> deliveredMessagesIdList = new List<string>();

        private int totalMessages = 0;

        [Test]
        public void SendSimpleSmsTest()
        {
            var gate = new Gate(new GateEvents());

            var server = gate.AddServerConnection("TestSrver", "sysId", "sysPass");
            var server2 = gate.AddServerConnection("AnotherTestSrver", "sysId2", "sysPass2");
            server.use_deliver_sm = false;
            server.registered_delivery = 1;

            Server.StartAsync("127.0.0.1", 2775);

            gate.Events.Event += Events_Event;
            gate.Events.ChannelEvent += Events_ChannelEvent;
            gate.Events.NewMessageEvent += Events_NewMessageEvent;
            gate.Events.MessageDeliveryReportEvent += Events_MessageDeliveryReportEvent;

            var client = gate.AddClientConnection("bg", "127.0.0.1", 2775, "sysId", "sysPass");
            client.debug = true;
            var client2 = gate.AddClientConnection("AnotherClient", "127.0.0.1", 2775, "sysId2", "sysPass2");

            // turn ON debug to show PDU
            // client.debug = true;

            // make timeout longer for debug, no impact on test time
            client.timeout = 30000;

            var c = client.Connect();
            var c2 = client2.Connect();

            Assert.IsTrue(c, "Connection should be established");
            Assert.IsTrue(c2, "2nd Connection should be established");

            Thread.Sleep(1000);

            // send SMS!
            client.SubmitSm(1, "47975091981", "97509181", "test Norsk og Svensk");
            server.SubmitSm(2, "4755555", "47666666", "test message from server");
            server.use_deliver_sm = true;

            server.SubmitSm(3, "4755555", "4777777777", "Test Unicode - Russian Египет plus norsk øæåØÆÅöÖ", "unicode");
            server.SubmitSm(7, "4755577", "4777755555", "test ascii with deliversm");

            server.SubmitSm(8, "4755577", "4777755555", "Norwegian char: æøåÆØÅ");

            client.large_message = 1;
            client.SubmitSm(4, "47111111", "47222222222",
                "123 Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message 123");
            server.SubmitSm(9, "466666666", "47700000000", "Test deliver_sm long message deliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long message 123 ");
            server.SubmitSm(10, "46333333", "47700000000", "Египет ЕгипетЕгипетЕгипетЕгипет eliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long message ЕгипетЕгипетЕгипетЕгипетЕгипетЕгипет 123", "unicode");

            // Send long message from server using payload
            server.large_message = 2;
            server.SubmitSm(5, "477777", "455555",
                "132 Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm 123");

            // Send message from second client
            client2.SubmitSm(6, "4722222222", "9751111111", "Message from second client");

            // Uncomment for performance test
            //DateTime start = DateTime.Now;
            //// performance test, send 200 messages
            //for (int i = 11; i <= 310; i++)
            //{
            //    client.SubmitSm(i, "55555555", "4444444", "Performance test");
            //}
            //DateTime end = DateTime.Now;

            //Assert.IsTrue((end - start).TotalMilliseconds < 1000, "should send at least 200 per second");
            //Debug.WriteLine("Total messages sent in " + (end - start).TotalMilliseconds);
            //Thread.Sleep(5000);
            //Assert.AreEqual(305, totalMessages, "Total messages sent should match");

            // Send delivery reports
            Thread.Sleep(1000);
            server.SubmitDeliveryReport("1", Common.MessageStatus.delivered_ACK_received);
            client.SubmitDeliveryReport("2", Common.MessageStatus.delivered_ACK_received);

            // give some time 
            Thread.Sleep(2000);

            client.Quit();
            client2.Quit();

            // Check channel activity
            Assert.IsTrue(logBuffer.Contains("Sending [bind_transceiver]"), "Log buffer should contain bind transceiver");
            Assert.IsTrue(logBuffer.Contains("Receiving [bind_transceiver_resp]"), "Log buffer should contain bind transceiver response");

            Assert.IsTrue(logBuffer.Contains("Sending [submit_sm]"), "Log buffer should contain submit_sm request for sending SMS");
            Assert.IsTrue(logBuffer.Contains("Receiving [submit_sm_resp]"), "Log buffer should contain submit_sm_resp response for SMS");

            Assert.IsTrue(logBuffer.Contains("Message received from 4755555 to 47666666"), "Client should receive message");
            Assert.IsTrue(logBuffer.Contains("Message received from 47975091981 to 97509181"), "Server should receive message");
            Assert.IsTrue(logBuffer.Contains("Sending [unbind]"), "Log buffer should contain unbind request");

            // Check delivery reports
            Assert.IsTrue(logBuffer.Contains("Message ref: 1 DELIVR"), "Client should receive delivery report");
            Assert.IsTrue(deliveredMessagesIdList.Contains("1"), "Message #1 should have delivered status");
            Assert.IsTrue(logBuffer.Contains("Message ref: 2 DELIVR"), "Server should receive delivery report");
            Assert.IsTrue(deliveredMessagesIdList.Contains("2"), "Message #2 should have delivered status");

            // Check received messages
            Assert.IsTrue(messages.Contains("test Norsk og Svensk"), "Message #1 should be received");
            Assert.IsTrue(messages.Contains("test message from server"), "Message #2 should be received");
            Assert.IsTrue(messages.Contains("Test Unicode - Russian Египет plus norsk øæåØÆÅöÖ"), "Message #3 - unicode, should be received");
            Assert.IsTrue(messages.Contains("123 Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message Very long message 123"),
                "Message #4 - VERY LONG, should be received");
            Assert.IsTrue(messages.Contains("132 Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm Server also wants to send long message with deliver_sm 123"),
                "Message #5 - VERY LONG from Server, should be received");
            Assert.IsTrue(messages.Contains("Message from second client"), "Message #6 should be received");
            Assert.IsTrue(messages.Contains("test ascii with deliversm"), "Message #7 should be received");
            Assert.IsTrue(messages.Contains("Norwegian char: æøåÆØÅ"), "Message #8 should be received");
            Assert.IsTrue(messages.Contains("Test deliver_sm long message deliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long message 123 "), "Message #9 should be received");
            Assert.IsTrue(messages.Contains("Египет ЕгипетЕгипетЕгипетЕгипет eliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long messagedeliver_sm long message ЕгипетЕгипетЕгипетЕгипетЕгипетЕгипет 123"), "Message #10 should be received");

            Thread.Sleep(1000);
            Server.Stop();
        }

        void Events_MessageDeliveryReportEvent(string responseMessageId, Common.MessageStatus status)
        {
            if (status == Common.MessageStatus.delivered_ACK_received)
            {
                deliveredMessagesIdList.Add(responseMessageId);
            }
        }

        void Events_NewMessageEvent(string channelName, string messageId, string sender, string recipient, string body, string bodyFormat, int registeredDelivery)
        {
            Debug.WriteLine(DateTime.Now + ": New Message Received on " + channelName + ". From " + sender + " to " + recipient);
            messages.Add(body);
            totalMessages++;
        }

        void Events_ChannelEvent(string channelName, string description, string pdu)
        {
            Debug.WriteLine(DateTime.Now + ": " + channelName + ": " + description + ". PDU: " + pdu);
            logBuffer.Add(description);
        }

        void Events_Event(Events.LogEvent.Level level, string description)
        {
            Debug.WriteLine(DateTime.Now + ": " + level + ": " + description);
        }
    }
}
