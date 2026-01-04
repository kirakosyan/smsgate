# smsgate
SMPP client and server library for .NET 10

## Requirements
- .NET 10 SDK

## Build
```bash
dotnet restore
dotnet build
```

## Test
```bash
dotnet test
```

## Quick start
```csharp
var gate = new Gate(new GateEvents());

// Server
var server = gate.AddServerConnection("TestServer", "sysId", "sysPass");
Server.StartAsync("127.0.0.1", 2775);

// Client
var client = gate.AddClientConnection("bg", "127.0.0.1", 2775, "sysId", "sysPass");
client.debug = true;
client.timeout = 30000;
var connected = client.Connect();

// Send SMS
client.SubmitSm(1, "47975091981", "97509181", "test Norsk og Svensk");
server.SubmitSm(2, "4755555", "47666666", "test message from server");
