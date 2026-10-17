using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Smpp;
using Smpp.Events;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<GateEvents>();
builder.Services.AddSingleton<SmppGateway>();
builder.Services.AddHostedService<ExchangeWorker>();
await builder.Build().RunAsync();

sealed class ExchangeWorker(SmppGateway gateway, IHostApplicationLifetime lifetime, ILogger<ExchangeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A local provider makes the example runnable without an account or stored credentials.
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var providerEvents = new GateEvents();
        var provider = new Gate(providerEvents).AddServerConnection("worker-provider", "system", "password");
        provider.UseGsm7 = true;
        provider.InboundMessageHandler = (message, _) => Task.FromResult(MessageAcknowledgement.Accept(message.MessageId));
        providerEvents.NewMessageEvent += (_, id, _, _, _, _, _) => provider.SubmitDeliveryReport(id, Common.MessageStatus.delivered_ACK_received);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.Events.MessageDeliveryStatusEvent += status => { if (status.IsComplete && status.Status == Common.MessageStatus.delivered_ACK_received) delivered.TrySetResult(); };
        try
        {
            Server.StartAsync("127.0.0.1", port);
            var client = gateway.AddClient(new SmppClientOptions
            {
                ChannelName = "provider", Host = "127.0.0.1", Port = port,
                SystemId = "system", Password = "password", UseGsm7 = true,
                SourceTon = 1, SourceNpi = 1, DestinationTon = 1, DestinationNpi = 1,
                Submission = new() { PdusPerSecond = 20, MaxInFlightPdus = 5, QueueCapacity = 100 }
            });
            client.Diagnostic += item => logger.LogInformation("SMPP {Channel} {Event} sequence {Sequence} status {Status}",
                item.ChannelName, item.Name, item.SequenceNumber, item.CommandStatus);
            if (!(await client.ConnectAndBindAsync(stoppingToken)).Bound) throw new InvalidOperationException("Bind failed.");
            var result = await client.SubmitAsync(new(42, "1000", "2000", "Worker service: hello €"), stoppingToken);
            if (!result.Accepted) throw new InvalidOperationException("Submission failed: " + result.Outcome);
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
            await gateway.StopAsync(stoppingToken);
            Console.WriteLine("Worker service exchange passed; submissions drained and clients stopped.");
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 1;
            throw;
        }
        finally { Server.Stop(); lifetime.StopApplication(); }
    }
}
