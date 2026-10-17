using Smpp;
using Smpp.Events;

if (args.Length != 2 || !int.TryParse(args[1], out int port)) throw new ArgumentException("Usage: InteropPeer server|client PORT");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var events = new GateEvents();
if (args[0] == "server")
{
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var server = new Gate(events).AddServerConnection("independent", "system", "password");
    server.UseGsm7 = true;
    server.InboundMessageHandler = (message, _) => Task.FromResult(message.Body == "hello €"
        ? MessageAcknowledgement.Accept("independent-42") : MessageAcknowledgement.Reject(1));
    events.NewMessageEvent += (_, id, _, _, body, _, _) =>
    {
        received.TrySetResult(body);
        server.SubmitDeliveryReport(id, Common.MessageStatus.delivered_ACK_received);
    };
    try
    {
        Server.StartAsync("127.0.0.1", port);
        Console.WriteLine("READY");
        await Console.In.ReadLineAsync(deadline.Token);
        if (await received.Task.WaitAsync(deadline.Token) != "hello €") throw new InvalidOperationException("Unexpected message.");
    }
    finally { Server.Stop(); }
}
else if (args[0] == "client")
{
    await using var client = new SmppClientOptions
    {
        Host = "127.0.0.1", Port = port, SystemId = "system", Password = "password", UseGsm7 = true,
        Submission = new() { MaxThrottleRetries = 1, ThrottleRetryDelay = TimeSpan.FromMilliseconds(20) }
    }.CreateClient(events);
    var delivery = new TaskCompletionSource<MessageDeliveryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
    events.MessageDeliveryStatusEvent += status =>
    {
        if (status.IsComplete && status.Status == Common.MessageStatus.delivered_ACK_received) delivery.TrySetResult(status);
    };
    if (!(await client.ConnectAndBindAsync(deadline.Token)).Bound) throw new InvalidOperationException("Independent peer rejected bind.");
    var result = await client.SubmitAsync(new(42, "1", "2", "hello €"), deadline.Token);
    if (!result.Accepted || result.Attempts != 2 || result.Parts.Single().ProviderMessageId != "independent-42")
        throw new InvalidOperationException("Independent peer response mismatch.");
    await delivery.Task.WaitAsync(deadline.Token);
    await client.StopAsync(deadline.Token);
}
else throw new ArgumentException("Unknown mode.");
Console.WriteLine("PASSED");
