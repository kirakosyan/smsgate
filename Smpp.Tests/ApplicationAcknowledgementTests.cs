using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class ApplicationAcknowledgementTests
    {
        private static TaskCompletionSource<T> Completion<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedAcknowledgementDoesNotPublishAcceptedMessage(bool controlled)
        {
            int received = 0;
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, _, _, _) => Interlocked.Increment(ref received);
            var connection = new ProbeConnection(events) { FailAcknowledgement = true };
            var handled = Completion<bool>();
            if (controlled) connection.InboundMessageHandler = (_, _) =>
            {
                handled.TrySetResult(true);
                return Task.FromResult(MessageAcknowledgement.Accept());
            };
            try
            {
                connection.MessageHandler(Encode("submit", "persisted")[0]);
                if (controlled) await handled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Task.Delay(100);
                connection.Drain();
                Assert.That(received, Is.Zero);
            }
            finally { connection.Quit(); }
        }

        [Test]
        public async Task AcceptanceBeforeDeadlineSurvivesWaitingForSessionLock()
        {
            using var pair = new SocketPair();
            using var lockHeld = new ManualResetEventSlim();
            using var releaseLock = new ManualResetEventSlim();
            var started = Completion<bool>();
            var finish = Completion<MessageAcknowledgement>();
            var connection = new ProbeConnection
            {
                AcknowledgementTimeout = 1000,
                InboundMessageHandler = (_, _) => { started.TrySetResult(true); return finish.Task; }
            };
            connection.Attach(pair.Local);
            Task holder = null;
            try
            {
                connection.MessageHandler(Encode("submit", "persisted")[0]);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                holder = Task.Run(() => connection.HoldSession(() => { lockHeld.Set(); releaseLock.Wait(); }));
                Assert.That(lockHeld.Wait(3000), Is.True);
                finish.SetResult(MessageAcknowledgement.Accept("stored"));
                await Task.Delay(1300);
                releaseLock.Set();
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.Zero);
            }
            finally { releaseLock.Set(); if (holder != null) await holder; connection.Quit(); }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public async Task StorageCompletesBeforeAcceptanceWhileKeepalivesRemainResponsive(string command)
        {
            using var pair = new SocketPair();
            var started = Completion<InboundMessage>();
            var stored = Completion<bool>();
            var received = Completion<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, id, _, _, _, _, _) => received.TrySetResult(id);
            var connection = new ProbeConnection(events)
            {
                InboundMessageHandler = async (message, token) =>
                {
                    started.TrySetResult(message);
                    await stored.Task.WaitAsync(token);
                    return MessageAcknowledgement.Accept("stored-message");
                }
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(Encode(command, "hello")[0]);
                var incoming = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(incoming.Body, Is.EqualTo("hello"));
                Assert.That(incoming.SequenceNumber, Is.EqualTo(42));
                connection.MessageHandler("00000010000000150000000000000077");
                Assert.That(ReadPdu(pair.Remote.GetStream()), Is.EqualTo("00000010800000150000000000000077"));
                Assert.That(received.Task.IsCompleted, Is.False);
                stored.SetResult(true);
                string response = ReadPdu(pair.Remote.GetStream());
                Assert.That(Convert.ToUInt32(response.Substring(8, 8), 16), Is.EqualTo((uint)incoming.Command | 0x80000000));
                Assert.That(Convert.ToUInt32(response.Substring(16, 8), 16), Is.Zero);
                Assert.That(new SubmitSmResp(response).MessageID, Is.EqualTo(command == "deliver" ? "" : "stored-message"));
                Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("stored-message"));
            }
            finally { connection.Quit(); }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void ApplicationCanRejectWithoutPublishingAnAcceptedMessage(string command)
        {
            using var pair = new SocketPair();
            int received = 0;
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, _, _, _) => Interlocked.Increment(ref received);
            var connection = new ProbeConnection(events)
            {
                InboundMessageHandler = (_, _) => Task.FromResult(MessageAcknowledgement.Reject(0x58))
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(Encode(command, "hello")[0]);
                string response = ReadPdu(pair.Remote.GetStream());
                connection.Drain();
                Assert.That(Convert.ToUInt32(response.Substring(16, 8), 16), Is.EqualTo(0x58));
                Assert.That(received, Is.Zero);
            }
            finally { connection.Quit(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HandlerFailureOrInvalidResponseIdReturnsSystemError(bool invalidId)
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection
            {
                InboundMessageHandler = (_, _) => invalidId
                    ? Task.FromResult(MessageAcknowledgement.Accept("invalid\0id"))
                    : throw new InvalidOperationException("Storage is down")
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(Encode("submit", "hello")[0]);
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.EqualTo(8));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public async Task NonCooperatingHandlerTimesOutAndKeepsItsConcurrencySlotUntilItFinishes()
        {
            using var pair = new SocketPair();
            var started = Completion<bool>();
            var finish = Completion<MessageAcknowledgement>();
            var cancelled = Completion<bool>();
            var connection = new ProbeConnection
            {
                AcknowledgementTimeout = 100, MaxPendingAcknowledgements = 1,
                InboundMessageHandler = async (_, token) =>
                {
                    using var registration = token.Register(() => cancelled.TrySetResult(true));
                    started.TrySetResult(true);
                    return await finish.Task; // Deliberately ignores cancellation.
                }
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(Encode("submit", "hello")[0]);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.EqualTo(8));
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                connection.MessageHandler(Encode("submit", "overloaded")[0]);
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.EqualTo(0x58));
                finish.SetResult(MessageAcknowledgement.Accept());
                connection.MessageHandler("00000010000000150000000000000077");
                Assert.That(ReadPdu(pair.Remote.GetStream()), Is.EqualTo("00000010800000150000000000000077"));
                pair.Remote.GetStream().ReadTimeout = 100;
                Assert.Throws<System.IO.IOException>(() => pair.Remote.GetStream().ReadByte(), "A late handler must not send a second response.");
            }
            finally { finish.TrySetResult(MessageAcknowledgement.Accept()); connection.Quit(); }
        }

        [Test]
        public async Task PendingPduLimitRejectsExcessWorkWithoutInvokingItsHandler()
        {
            using var pair = new SocketPair();
            var started = Completion<bool>();
            var finish = Completion<MessageAcknowledgement>();
            int calls = 0;
            var connection = new ProbeConnection
            {
                MaxPendingAcknowledgements = 1,
                InboundMessageHandler = (_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    started.TrySetResult(true);
                    return finish.Task;
                }
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(Encode("submit", "first")[0]);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                connection.MessageHandler(Encode("submit", "second")[0]);
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.EqualTo(0x58));
                Assert.That(calls, Is.EqualTo(1));
                finish.SetResult(MessageAcknowledgement.Accept());
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.Zero);
            }
            finally { finish.TrySetResult(MessageAcknowledgement.Accept()); connection.Quit(); }
        }

        [Test]
        public async Task DisconnectCancelsAcceptanceAndLateSuccessCannotAcknowledgeTheReplacementSession()
        {
            using var first = new SocketPair();
            using var replacement = new SocketPair();
            var started = Completion<bool>();
            var cancelled = Completion<bool>();
            var finish = Completion<MessageAcknowledgement>();
            var connection = new ProbeConnection
            {
                InboundMessageHandler = async (_, token) =>
                {
                    using var registration = token.Register(() => cancelled.TrySetResult(true));
                    started.TrySetResult(true);
                    return await finish.Task;
                }
            };
            try
            {
                connection.Attach(first.Local);
                connection.MessageHandler(Encode("submit", "old-session")[0]);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                connection.Drop();
                await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
                connection.Attach(replacement.Local);
                finish.SetResult(MessageAcknowledgement.Accept());
                connection.MessageHandler("00000010000000150000000000000077");
                Assert.That(ReadPdu(replacement.Remote.GetStream()), Is.EqualTo("00000010800000150000000000000077"));
                replacement.Remote.GetStream().ReadTimeout = 100;
                Assert.Throws<System.IO.IOException>(() => replacement.Remote.GetStream().ReadByte());
            }
            finally { finish.TrySetResult(MessageAcknowledgement.Accept()); connection.Quit(); }
        }

        [Test]
        public void MultipartAcceptanceSeesEachSegmentAndPublishesOnlyAfterAllAcceptedSegments()
        {
            using var pair = new SocketPair();
            var segments = new ConcurrentQueue<InboundMessage>();
            var assembled = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => assembled.Enqueue(body);
            var connection = new ProbeConnection(events)
            {
                InboundMessageHandler = (message, _) =>
                {
                    segments.Enqueue(message);
                    return Task.FromResult(MessageAcknowledgement.Accept("part-" + message.PartNumber));
                }
            };
            try
            {
                connection.Attach(pair.Local);
                var frames = Encode("submit", new string('a', 161));
                connection.MessageHandler(frames[1]);
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).MessageID, Is.EqualTo("part-2"));
                connection.Drain();
                Assert.That(assembled, Is.Empty);
                connection.MessageHandler(frames[0]);
                Assert.That(new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).MessageID, Is.EqualTo("part-1"));
                WaitUntil(() => assembled.Count == 1, "Accepted segments should form one complete application message");
                Assert.That(assembled.TryPeek(out string text), Is.True);
                Assert.That(text, Is.EqualTo(new string('a', 161)));
                Assert.That(segments.TryPeek(out var part), Is.True);
                Assert.That(part.PartNumber, Is.EqualTo(2));
                Assert.That(part.PartCount, Is.EqualTo(2));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void ReceiptHandlerCanRejectBeforeDeliveryEventsArePublished()
        {
            using var pair = new SocketPair();
            int receipts = 0;
            var events = new GateEvents();
            events.DeliveryReceiptEvent += _ => Interlocked.Increment(ref receipts);
            var connection = new ProbeConnection(events)
            {
                InboundMessageHandler = (message, _) =>
                {
                    Assert.That(message.Receipt.ProviderMessageId, Is.EqualTo("provider"));
                    Assert.That(message.OptionalParameters["001e"], Is.Not.Empty);
                    Assert.That(message.OptionalParameters["0423"], Is.EqualTo("03002A"));
                    return Task.FromResult(MessageAcknowledgement.Reject(0x14));
                }
            };
            try
            {
                connection.Attach(pair.Local);
                connection.MessageHandler(DeliveryTrackingTests.Receipt("provider", 2));
                Assert.That(new DeliverSmResp(ReadPdu(pair.Remote.GetStream())).command_status, Is.EqualTo(0x14));
                connection.Drain();
                Assert.That(receipts, Is.Zero);
            }
            finally { connection.Quit(); }
        }
    }
}
