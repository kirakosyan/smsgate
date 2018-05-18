# smsgate
SMPP client and server

# Server start example
var gate = new Gate(new GateEvents());
var server = gate.AddServerConnection("TestSrver", "sysId", "sysPass");
Server.StartAsync("127.0.0.1", 2775);

#Client
var client = gate.AddClientConnection("bg", "127.0.0.1", 2775, "sysId", "sysPass");
client.debug = true;
var client2 = gate.AddClientConnection("AnotherClient", "127.0.0.1", 2775, "sysId", "sysPass");

// turn ON debug to show PDU
// client.debug = true;

// make timeout longer for debug, no impact on test time
client.timeout = 30000;

var c = client.Connect();

# send sms
client.SubmitSm(1, "47975091981", "97509181", "test Norsk og Svensk");
server.SubmitSm(2, "4755555", "47666666", "test message from server");
