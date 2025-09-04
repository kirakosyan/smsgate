# smsgate
SMPP client and server library for .NET 8

## Requirements
- .NET 8.0 SDK or later

## Building the Project
```bash
dotnet restore
dotnet build
```

## Running Tests
```bash
dotnet test
```

## Server start example
```csharp
var gate = new Gate(new GateEvents());
var server = gate.AddServerConnection("TestServer", "sysId", "sysPass");
Server.StartAsync("127.0.0.1", 2775);
```

## Client
```csharp
var client = gate.AddClientConnection("bg", "127.0.0.1", 2775, "sysId", "sysPass");
client.debug = true;
var client2 = gate.AddClientConnection("AnotherClient", "127.0.0.1", 2775, "sysId", "sysPass");

// turn ON debug to show PDU
// client.debug = true;

// make timeout longer for debug, no impact on test time
client.timeout = 30000;

var c = client.Connect();
```

## Send SMS
```csharp
client.SubmitSm(1, "47975091981", "97509181", "test Norsk og Svensk");
server.SubmitSm(2, "4755555", "47666666", "test message from server");
```
