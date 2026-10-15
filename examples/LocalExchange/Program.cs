using Smpp;
using Smpp.Events;

const string fromClient = "Hello from client";
const string fromServer = "Hello from server: øæå Привет";
var deadline = TimeSpan.FromSeconds(10);
var serverReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var clientReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var acknowledged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var eventsHub = new GateEvents();

eventsHub.NewMessageEvent += (channel, id, sender, recipient, body, format, receipt) =>
{
    if (channel == "server-demo") serverReceived.TrySetResult(body);
    if (channel == "client-demo") clientReceived.TrySetResult(body);
};
eventsHub.MessageStatusEvent += (id, status, responseId) =>
{
    if (id == 1 && status == Common.MessageStatus.sent) acknowledged.TrySetResult(responseId);
};
eventsHub.MessageDeliveryReportEvent += (id, status) =>
{
    if (status == Common.MessageStatus.delivered_ACK_received) delivered.TrySetResult(id);
};

var gate = new Gate(eventsHub);
// Demonstration credentials for this loopback listener only.
var server = gate.AddServerConnection("server-demo", "demo-system", "demo-password");
server.use_deliver_sm = true;
Client? client = null;
try
{
    Server.StartAsync("127.0.0.1", 2775);
    client = gate.AddClientConnection("client-demo", "127.0.0.1", 2775, "demo-system", "demo-password");
    client.direction_in = true;
    client.direction_out = true;
    if (!await client.ConnectAsync() ||
        !SpinWait.SpinUntil(() => client.connected && server.connected, deadline))
        throw new InvalidOperationException("The local SMPP session did not bind.");

    if (client.SubmitSm(1, "15550001", "15550002", fromClient, "ascii", true) != 0)
        throw new InvalidOperationException("The client submission failed.");
    if (await serverReceived.Task.WaitAsync(deadline) != fromClient)
        throw new InvalidOperationException("The server received an unexpected message.");
    var messageId = await acknowledged.Task.WaitAsync(deadline);
    Console.WriteLine($"Server received: {fromClient}");

    if (server.SubmitSm(2, "15550002", "15550001", fromServer, "unicode", false) != 0)
        throw new InvalidOperationException("The server submission failed.");
    if (await clientReceived.Task.WaitAsync(deadline) != fromServer)
        throw new InvalidOperationException("The client received an unexpected Unicode message.");
    Console.WriteLine($"Client received: {fromServer}");

    server.SubmitDeliveryReport(messageId, Common.MessageStatus.delivered_ACK_received);
    if (await delivered.Task.WaitAsync(deadline) != messageId)
        throw new InvalidOperationException("The delivery receipt did not match the submission.");
    Console.WriteLine($"Delivery receipt confirmed: {messageId}");
    Console.WriteLine("Local SMPP exchange passed.");
}
finally
{
    client?.Quit();
    server.Quit();
    Server.Stop();
}
