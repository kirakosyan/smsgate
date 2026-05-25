using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Smpp.Events;

namespace Smpp.Tests
{
    [TestFixture]
    public class ConnectionBehaviorTests
    {
        [SetUp]
        public void SetUp()
        {
            Gate.Clients.Clear();
            Gate.Servers.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Gate.Clients.Clear();
            Gate.Servers.Clear();
        }

        [Test]
        public void KeepAlivePropertyUpdatesEnquireLinkTimerInterval()
        {
            var client = new Client("timer-keepalive", new GateEvents());

            client.keep_alive = 1234;

            var timer = GetTimer(client, "enquire_link_timer");
            Assert.That(timer.Interval, Is.EqualTo(1234));
        }

        [Test]
        public void TimeoutPropertyUpdatesTimeoutTimerInterval()
        {
            var client = new Client("timer-timeout", new GateEvents());

            client.timeout = 4321;

            var timer = GetTimer(client, "timeout_timer");
            Assert.That(timer.Interval, Is.EqualTo(4321));
        }

        [Test]
        public void ConnectReturnsWithoutBlockingOnInitialSocketFailure()
        {
            int closedPort = GetClosedLoopbackPort();
            var client = new Client("connect-failure", new GateEvents())
            {
                host = "127.0.0.1",
                port = closedPort,
                system_id = "sys",
                password = "pass",
                reconnectDelay = 500
            };

            var stopwatch = Stopwatch.StartNew();
            var connected = client.Connect();
            stopwatch.Stop();

            Assert.That(connected, Is.False);
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), "Connect should fail fast and schedule reconnect asynchronously");

            Gate.Clients.Remove("connect-failure");
            Thread.Sleep(700);
        }

        [Test]
        public void ScheduleReconnectSkipsMissingClientRegistration()
        {
            var events = new GateEvents();
            var reconnectSkipped = new ManualResetEventSlim();

            events.ChannelEvent += (channelName, description, pdu) =>
            {
                if (description == "Reconnect skipped: client is no longer registered")
                {
                    reconnectSkipped.Set();
                }
            };

            var client = new TestClient("removed-client", events);
            Gate.Clients.Remove("removed-client");

            client.ScheduleReconnectImmediately();

            Assert.That(reconnectSkipped.Wait(1000), Is.True, "Reconnect should be skipped cleanly when the client is no longer registered");
        }

        private static System.Timers.Timer GetTimer(Connection connection, string fieldName)
        {
            var field = typeof(Connection).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected timer field '{fieldName}' to exist");

            var timer = field.GetValue(connection) as System.Timers.Timer;
            Assert.That(timer, Is.Not.Null, $"Expected '{fieldName}' to contain a timer instance");

            return timer;
        }

        private static int GetClosedLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }

        private sealed class TestClient : Client
        {
            public TestClient(string channelName, GateEvents events) : base(channelName, events)
            {
            }

            public void ScheduleReconnectImmediately()
            {
                ScheduleReconnect(0);
            }
        }
    }
}