using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Smpp;
using Smpp.Events;

internal static class Program
{
    private static async Task Main()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        int port;
        try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
        finally { reservation.Stop(); }
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<MessageDeliveryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerEvents = new GateEvents();
        var provider = new Gate(providerEvents).AddServerConnection("framework-provider", "system", "password");
        provider.UseGsm7 = true;
        provider.InboundMessageHandler = (message, _) => Task.FromResult(MessageAcknowledgement.Accept(message.MessageId));
        providerEvents.NewMessageEvent += (_, _, _, _, body, _, _) => received.TrySetResult(body);
        var events = new GateEvents();
        events.MessageDeliveryStatusEvent += status => { if (status.IsComplete) delivered.TrySetResult(status); };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var gateway = new SmppGateway(events);
        try
        {
            Server.StartAsync("127.0.0.1", port);
            var client = gateway.AddClient(new SmppClientOptions
            {
                ChannelName = "framework-client", Host = "127.0.0.1", Port = port,
                SystemId = "system", Password = "password", UseGsm7 = true,
                Submission = new SubmissionOptions { QueueCapacity = 10, MaxInFlightPdus = 2, PdusPerSecond = 20 }
            });
            if (!(await client.ConnectAndBindAsync(deadline.Token)).Bound) throw new InvalidOperationException("Bind failed.");
            string text = new string('€', 81);
            var submission = await client.SubmitAsync(new OutboundMessage(42, "1000", "2000", text), deadline.Token);
            if (!submission.Accepted || submission.Parts.Count != 2 || submission.Parts.Any(part => string.IsNullOrEmpty(part.ProviderMessageId)))
                throw new InvalidOperationException("Multipart submission failed.");
            if (await BeforeDeadline(received.Task, deadline.Token) != text) throw new InvalidOperationException("Message content changed.");
            foreach (var part in submission.Parts)
                provider.SubmitDeliveryReport(part.ProviderMessageId, Common.MessageStatus.delivered_ACK_received);
            var delivery = await BeforeDeadline(delivered.Task, deadline.Token);
            if (delivery.Status != Common.MessageStatus.delivered_ACK_received || delivery.Parts.Count != 2)
                throw new InvalidOperationException("Delivery tracking failed.");
            await gateway.StopAsync(deadline.Token);
            Console.WriteLine("Framework package exchange passed: modern async API, two GSM-7 parts, provider IDs, complete delivery, graceful stop.");
        }
        finally { Server.Stop(); }
    }

    private static async Task<T> BeforeDeadline<T>(Task<T> task, CancellationToken cancellation)
    {
        if (await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellation)) != task)
            throw new OperationCanceledException(cancellation);
        return await task;
    }
}
