<h1 style="text-align: center;"> SimpleNetworking </h1>

<p style="text-align: center;"> SimpleNetworking is a general purpose tcp/udp communication library for C# </p>

## About the project
### Why
I created this project in my free time in preparation for my IT studies that will start soon. <br>
This is my first time working with networking so my code is a bit of a mess. <br>
I would love to get some feedback and ideas for improvements. <br>
### What for
Its made for general use in all kind of applications or games. <br>
To use the messages you subscribe to an event.

## Usage 
### Server
To use you first need do have a server to connect to or create one yourself. <br> 
First the usings: <br>

```
using SimpleNetworking;
using SimpleNetworking.Server;
```

The syntax to create a server is as follows: <br>
`SimpleServer server = new SimpleServer(Protocol, max clients, port, ipaddress)`

for example: <br>
`SimpleServer server = new SimpleServer(Protocol.Tcp, 5, 12345, IPAddress.Any)` <br>

To start listening for incoming connections and messages call: <br> `server.StartListen(CancellationTokenSource.Token)` <br>

To be able to use the messsages you need to subscribe to the OnMessageReceived event: <br>
```
server.OnMessageReceived += (e) =>
{
    Console.WriteLine($"Server received message: '{e.MessageString}' from {e.Sender.Address}:{e.Sender.Port}");
};
```

To send messages you call either <br>
`server.SendToAll(byte[] message)` <br>
or  <br>
`server.SendTo(byte[] message, IPEndPoint remoteEndPoint)`  <br>

### Client

Usings: 
```
using SimpleNetworking;
using SimpleNetworking.Client;
```

Creation: <br>
`SimpleClient client = new SimpleClient(Protocol.Tcp, 12345, IPAddress.Loopback)` <br>

Connect and start listening: <br>
`client.Connect(CancellationTokenSource.Token)` <br>

To send messages: <br>
`client.Send(byte[] data)` <br>
or <br>
`client.SendAsync(byte[] data)`