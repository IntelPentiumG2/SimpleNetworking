﻿using SimpleNetworking.EventArgs;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SimpleNetworking.Server
{
    /// <summary>
    /// A simple server that can handle TCP and UDP connections
    /// </summary>
    public class SimpleServer : IDisposable
    {
        /// <summary>
        /// Byte array representing the end of message
        /// </summary>
        private readonly byte[] eomBytes = Encoding.UTF8.GetBytes("<EOM>");
        /// <summary>
        /// The default listening socket
        /// </summary>
        private readonly Socket listenSocket;
        /// <summary>
        /// The max amount of connections allowed
        /// </summary>
        private readonly int maxConnections;
        /// <summary>
        /// The sequence number to send with the message
        /// </summary>
        private byte sendSequenceNumber = 1;
        /// <summary>
        /// Gets the local endpoint of the server
        /// </summary>
        public readonly IPEndPoint LocalEndPoint;
        /// <summary>
        /// Dictionary containing the last sequence number received from a specific client
        /// </summary>
        private readonly Dictionary<EndPoint, byte>? clientLastSequenceNumbers;

        private int EomLength => eomBytes.Length;
        private readonly int prefixLength;

        /// <summary>
        /// The list of connected TCP sockets
        /// </summary>
        private readonly List<Socket>? connectedSockets;
        private readonly List<IPEndPoint>? udpRemoteEndPoints;
        private readonly SemaphoreSlim semaphore;

        private bool TryGetTcpSocketByEndPoint(IPEndPoint iPEnd, out Socket? socket)
        {
            if (Protocol != Protocol.Tcp)
                throw new InvalidOperationException("This method is only available for TCP servers.");

            socket = connectedSockets!.Find(s => s.Connected && ((IPEndPoint)s.RemoteEndPoint!).Address.Equals(iPEnd.Address));
            return socket != null;
        }

        /// <summary>
        /// Gets the protocol used by the server
        /// </summary>
        public Protocol Protocol { get; }
        /// <summary>
        /// Gets if the server with the specified remote endpoint is connected
        /// </summary>
        /// <param name="remoteEndPoint">The remote endpoint to check for</param>
        /// <returns>true if the client is connected, otherwise false</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IsTcpConnected(IPEndPoint remoteEndPoint)
        {
            if (Protocol != Protocol.Tcp)
                throw new InvalidOperationException("This method is only available for TCP servers.");

            return connectedSockets!.Exists(socket => socket.Connected && socket.RemoteEndPoint!.Equals(remoteEndPoint));
        }
        /// <summary>
        /// Gets if the udp server is connected
        /// </summary>
        public bool IsUdpConnected => listenSocket.Connected;
        /// <summary>
        /// Fires when a message is received
        /// </summary>
        public event Action<MessageReceivedEventArgs>? OnMessageReceived;
        /// <summary>
        /// Fires when a client connects
        /// </summary>
        public event Action<ClientConnectedEventArgs>? OnClientConnected;
        /// <summary>
        /// Fires when a client disconnects
        /// </summary>
        public event Action<ClientDisconnectedEventArgs>? OnClientDisconnected;

        /// <summary>
        /// Creates a new instance of the SimpleServer class
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="maxConnections">The max amounts of clients allowed to connect</param>
        /// <param name="port">The port to listen on</param>
        /// <param name="ip">The ip to listen on</param>
        /// <exception cref="ArgumentException"></exception>
        public SimpleServer(Protocol protocol, int maxConnections, int port, IPAddress? ip = null)
        {
            Protocol = protocol;
            this.maxConnections = maxConnections > 0 ? maxConnections : 1;
            LocalEndPoint = new IPEndPoint(ip ?? IPAddress.Any, port);
            semaphore = new SemaphoreSlim(maxConnections, maxConnections);

            switch (protocol)
            {
                case Protocol.Tcp:
                    listenSocket = new Socket(ip?.AddressFamily ?? AddressFamily.Unspecified, SocketType.Stream, ProtocolType.Tcp);
                    connectedSockets = new(maxConnections);
                    break;
                case Protocol.Udp:
                    udpRemoteEndPoints = [];
                    clientLastSequenceNumbers = [];
                    listenSocket = new Socket(ip?.AddressFamily ?? AddressFamily.Unspecified, SocketType.Dgram, ProtocolType.Udp);
                    break;
                default:
                    throw new ArgumentException("Invalid protocol");
            }

            prefixLength = Global.PrefixLength(Protocol);
            listenSocket.Bind(LocalEndPoint);
        }

        /// <summary>
        /// Creates a new instance of the SimpleServer class
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="maxConnections">The max amounts of clients allowed to connect</param>
        /// <param name="port">The port to listen on</param>
        /// <param name="ip">The ip to listen on</param>
        /// <exception cref="ArgumentException"></exception>
        public SimpleServer(Protocol protocol, int maxConnections, int port, string ip) : this(protocol, maxConnections, port, IPAddress.Parse(ip)) { }

        /// <summary>
        /// Creates a new instance of the SimpleServer class
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="maxConnections">The max amounts of clients allowed to connect</param>
        /// <param name="localEndPoint">The local endpoint to listen on</param>
        /// <exception cref="ArgumentException"></exception>
        public SimpleServer(Protocol protocol, int maxConnections, IPEndPoint localEndPoint) : this(protocol, maxConnections, localEndPoint.Port, localEndPoint.Address) { }

        /// <summary>
        /// Starts listening for incoming connections and messages
        /// </summary>
        /// <param name="token">Token to cancel listening with</param>
        /// <exception cref="ArgumentException"></exception>
        public void StartListen(CancellationToken token)
        {
            _ = Protocol switch
            {
                Protocol.Tcp => AcceptTcpClient(token),
                Protocol.Udp => Listen(listenSocket, token),
                _ => throw new ArgumentException("Invalid protocol"),
            };
        }

        /// <summary>
        /// Awaits incoming tcp connections, adds them to the connectedSockets list and starts listening for messages
        /// </summary>
        /// <param name="token">The cancellation token to cancel listening for connections</param>
        /// <returns>A task listening for incoming tcp connections</returns>
        private async Task AcceptTcpClient(CancellationToken token)
        {
            listenSocket.Listen(maxConnections);

            while (!token.IsCancellationRequested)
            {
                await semaphore.WaitAsync(token);

                Socket socket = await listenSocket.AcceptAsync(token);
                await socket.SendAsync(Encoding.UTF8.GetBytes("Connected to server"));
                connectedSockets!.Add(socket);
                OnClientConnected?.Invoke(new ClientConnectedEventArgs(socket, DateTime.Now));

                _ = Listen(socket, token);
            }
        }

        /// <summary>
        /// Listens for incoming messages on the specified socket
        /// </summary>
        /// <param name="socket">The socket to listen on</param>
        /// <param name="token">The cancellation token</param>
        /// <returns>A task listening for messages</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task Listen(Socket socket, CancellationToken token)
        {
            List<byte> truncationBuffer = [];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(socket.ReceiveBufferSize);
                    int received;
                    SocketReceiveFromResult? result = null;

                    switch (socket.ProtocolType)
                    {
                        case ProtocolType.Tcp:
                            received = await socket.ReceiveAsync(receiveBuffer, SocketFlags.None, token);
                            break;
                        case ProtocolType.Udp:
                            result = await socket.ReceiveFromAsync(receiveBuffer, SocketFlags.None, LocalEndPoint, token);
                            received = result.Value.ReceivedBytes;

                            if (udpRemoteEndPoints!.Count >= maxConnections && !udpRemoteEndPoints!.Contains((IPEndPoint)result.Value.RemoteEndPoint))
                            {
                                Debug.WriteLine("Max connections reached. Ignoring incoming connection.");
                                ArrayPool<byte>.Shared.Return(receiveBuffer);
                                continue;
                            }

                            if (!udpRemoteEndPoints!.Contains((IPEndPoint)result.Value.RemoteEndPoint))
                            {
                                udpRemoteEndPoints.Add((IPEndPoint)result.Value.RemoteEndPoint);
                                clientLastSequenceNumbers!.Add(result.Value.RemoteEndPoint, 0);
                                OnClientConnected?.Invoke(new ClientConnectedEventArgs(listenSocket, DateTime.Now));
                            }
                            break;
                        default:
                            ArrayPool<byte>.Shared.Return(receiveBuffer);
                            throw new InvalidOperationException("Invalid protocol type");
                    }

                    if (received == 0)
                    {
                        ArrayPool<byte>.Shared.Return(receiveBuffer);
                        break;
                    }

                    Memory<byte> buffer;

                    if (truncationBuffer.Count > 0)
                    {
                        truncationBuffer.AddRange(receiveBuffer[..received]);
                        buffer = truncationBuffer.ToArray().AsMemory();
                        truncationBuffer.Clear();
                    }
                    else
                    {
                        buffer = receiveBuffer.AsMemory(0, received);
                    }

                    int messageLength;
                    while (buffer.Span.Length > prefixLength && (messageLength = BitConverter.ToUInt16(buffer.Span)) <= buffer.Span.Length - (prefixLength + EomLength))
                    {
                        if (messageLength > socket.ReceiveBufferSize)
                        {
                            Debug.WriteLine("Could not identify message length. Ignoring.");
                            buffer = buffer[(messageLength + EomLength + prefixLength)..];
                            continue;
                        }

                        Memory<byte> message = buffer[prefixLength..(messageLength + prefixLength + EomLength)];

                        if (!message.Span.EndsWith(eomBytes))
                        {
                            Debug.WriteLine("Message end delimiter missing. Ignoring.");
                            buffer = buffer[(messageLength + EomLength + prefixLength)..];
                            continue;
                        }

                        message = message[..^EomLength];

                        if (socket.ProtocolType == ProtocolType.Udp)
                        {
                            byte lastSequenceNumber = clientLastSequenceNumbers![result!.Value.RemoteEndPoint];
                            byte sequenceNumber = buffer.Span[2];

                            if (sequenceNumber != lastSequenceNumber + 1
                                && !(sequenceNumber == 0 && lastSequenceNumber == 255))
                            {
                                Debug.WriteLine($"Packet loss detected. Client skipping packet {sequenceNumber}. " +
                                    $"Waiting for packet: {lastSequenceNumber + 1}");

                                buffer = buffer[(messageLength + eomBytes.Length + prefixLength)..];

                                continue;
                            }

                            clientLastSequenceNumbers![result.Value.RemoteEndPoint] = sequenceNumber;
                        }

                        IPEndPoint endPoint = socket.ProtocolType switch
                        {
                            ProtocolType.Tcp => (IPEndPoint)socket.RemoteEndPoint!,
                            ProtocolType.Udp => (IPEndPoint)result!.Value.RemoteEndPoint,
                            _ => throw new InvalidOperationException("Invalid protocol type")
                        };

                        // Invoke message received event
                        OnMessageReceived?.Invoke(new MessageReceivedEventArgs(message.ToArray(), endPoint));
                        buffer = buffer[(messageLength + eomBytes.Length + prefixLength)..];
                    }

                    // Remaining bytes are part of the next message or are truncated
                    if (!buffer.IsEmpty)
                    {
                        truncationBuffer.AddRange(buffer.Span);
                        buffer.Span.Clear();
                        Debug.WriteLine($"Truncated on server: {Encoding.UTF8.GetString(truncationBuffer.ToArray())}.");
                    }

                    ArrayPool<byte>.Shared.Return(receiveBuffer);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error on server: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
            finally
            {
                switch (socket.ProtocolType)
                {
                    case ProtocolType.Udp:
                        udpRemoteEndPoints!.Remove((IPEndPoint)socket.RemoteEndPoint!);
                        clientLastSequenceNumbers!.Remove(socket.RemoteEndPoint!);
                        break;
                    case ProtocolType.Tcp:
                        connectedSockets!.Remove(socket);
                        break;
                }
                
                truncationBuffer.Clear();

                semaphore.Release();
                OnClientDisconnected?.Invoke(new ClientDisconnectedEventArgs(socket, DateTime.Now, $"Client {(socket.RemoteEndPoint as IPEndPoint)!.Address}:{(socket.RemoteEndPoint as IPEndPoint)!.Port} disconnected"));
                socket.Dispose();
            }
        }

        /// <summary>
        /// Sends a message to a specific client
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <param name="remoteEndPoint">The IPEndPoint to send the data to</param>
        public void SendTo(string message, IPEndPoint remoteEndPoint) => SendTo(Encoding.UTF8.GetBytes(message), remoteEndPoint);

        /// <summary>
        /// Sends a message to a specific client
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <param name="remoteEndPoint">The IPEndPoint to send the data to</param>
        public void SendTo(byte[] message, IPEndPoint remoteEndPoint)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)message.Length));
                if (Protocol == Protocol.Udp)
                {
                    byte[] messageWithEom = [..lengthBytes, sendSequenceNumber++, .. message, .. eomBytes];
                    listenSocket.SendTo(messageWithEom, remoteEndPoint);
                }
                else if (Protocol == Protocol.Tcp && TryGetTcpSocketByEndPoint(remoteEndPoint, out Socket? socket))
                {
                    socket!.Send([.. lengthBytes, .. message, .. eomBytes]);
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a message to a specific client asynchronously
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <param name="remoteEndPoint">The IPEndPoint to send the data to</param>
        /// <returns>A task sending the data</returns>
        public async Task SendToAsync(string message, IPEndPoint remoteEndPoint) => await SendToAsync(Encoding.UTF8.GetBytes(message), remoteEndPoint);

        /// <summary>
        /// Sends a message to a specific client asynchronously
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <param name="remoteEndPoint">The IPEndPoint to send the data to</param>
        /// <returns>A task sending the data</returns>
        public async Task SendToAsync(byte[] message, IPEndPoint remoteEndPoint)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)message.Length));
                switch (Protocol)
                {
                    case Protocol.Tcp:
                        message = [.. lengthBytes, .. message, .. eomBytes];
                        if (TryGetTcpSocketByEndPoint(remoteEndPoint, out Socket? socket))
                        {
                            await socket!.SendAsync(message);
                        }
                        break;
                    case Protocol.Udp:
                        message = [.. lengthBytes, sendSequenceNumber++, .. message, .. eomBytes];
                        await listenSocket.SendToAsync(message, remoteEndPoint);
                        break;
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a message to all connected clients
        /// </summary>
        /// <param name="message">The data to send</param>
        public void SendToAll(string message) => SendToAll(Encoding.UTF8.GetBytes(message));

        /// <summary>
        /// Sends a message to all connected clients
        /// </summary>
        /// <param name="message">The data to send</param>
        public void SendToAll(byte[] message)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)message.Length));
                if (Protocol == Protocol.Udp)
                {
                    byte[] messageWithEom = [.. lengthBytes, sendSequenceNumber++, .. message, .. eomBytes];
                    foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                    {
                        listenSocket.SendTo(messageWithEom, remoteEndPoint);
                    }
                }
                else if (Protocol == Protocol.Tcp)
                {
                    foreach (Socket socket in connectedSockets!)
                    {
                        socket.Send([.. lengthBytes, .. message, .. eomBytes]);
                    }
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a message to all connected clients asynchronously
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <returns>A task sending the message</returns>
        public async Task SendToAllAsync(string message) => await SendToAllAsync(Encoding.UTF8.GetBytes(message));

        /// <summary>
        /// Sends a message to all connected clients asynchronously
        /// </summary>
        /// <param name="message">The data to send</param>
        /// <returns>A task sending the message</returns>
        public async Task SendToAllAsync(byte[] message)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)message.Length));
                switch (Protocol)
                {
                    case Protocol.Udp:
                        message = [.. lengthBytes, sendSequenceNumber++, .. message, .. eomBytes];
                        foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                        {
                            await listenSocket.SendToAsync(message, remoteEndPoint);
                        }
                        break;
                    case Protocol.Tcp:
                        message = [.. lengthBytes, .. message, .. eomBytes];
                        foreach (Socket socket in connectedSockets!)
                        {
                            await socket.SendAsync(message);
                        }
                        break;
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a message to all connected clients except the ones specified
        /// </summary>
        /// <param name="data">The message to send</param>
        /// <param name="ipendpoints">The ipendpoints to exclude</param>
        public void SendToExcept(string data, IPEndPoint[] ipendpoints) => SendToExcept(Encoding.UTF8.GetBytes(data), ipendpoints);

        /// <summary>
        /// Sends a message to all connected clients except the ones specified
        /// </summary>
        /// <param name="data">The message to send</param>
        /// <param name="ipendpoints">The ipendpoints to exclude</param>
        public void SendToExcept(byte[] data, IPEndPoint[] ipendpoints)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)data.Length));
                if (Protocol == Protocol.Udp)
                {
                    byte[] messageWithEom = [.. lengthBytes, sendSequenceNumber++, .. data, .. eomBytes];
                    foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                    {
                        if (!ipendpoints.Contains(remoteEndPoint))
                        {
                            listenSocket.SendTo(messageWithEom, remoteEndPoint);
                        }
                    }
                }
                else if (Protocol == Protocol.Tcp)
                {
                    foreach (Socket socket in connectedSockets!)
                    {
                        if (!Array.Exists(ipendpoints, ep => ep.Address.Equals((socket.RemoteEndPoint as IPEndPoint)!.Address)))
                        {
                            socket.Send([.. lengthBytes, .. data, .. eomBytes]);
                        }
                    }
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Sends a message to all connected clients except the ones specified asynchronously
        /// </summary>
        /// <param name="data">The message to send</param>
        /// <param name="ipendpoints">The ipendpoints to exclude</param>
        /// <returns></returns>
        public async Task SendToExceptAsync(string data, IPEndPoint[] ipendpoints) => await SendToExceptAsync(Encoding.UTF8.GetBytes(data), ipendpoints);

        /// <summary>
        /// Sends a message to all connected clients except the ones specified asynchronously
        /// </summary>
        /// <param name="data">The message to send</param>
        /// <param name="ipendpoints">The ipendpoints to exclude</param>
        /// <returns></returns>
        public async Task SendToExceptAsync(byte[] data, IPEndPoint[] ipendpoints)
        {
            try
            {
                byte[] lengthBytes = BitConverter.GetBytes(((ushort)data.Length));
                if (Protocol == Protocol.Udp)
                {
                    byte[] messageWithEom = [.. lengthBytes, sendSequenceNumber++, .. data, .. eomBytes];
                    foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                    {
                        if (!ipendpoints.Contains(remoteEndPoint))
                        {
                            await listenSocket.SendToAsync(messageWithEom, remoteEndPoint);
                        }
                    }
                }
                else if (Protocol == Protocol.Tcp)
                {
                    foreach (Socket socket in connectedSockets!)
                    {
                        if (Array.Exists(ipendpoints, ep => ep.Address.Equals((socket.RemoteEndPoint as IPEndPoint)!.Address)))
                        {
                            continue;
                        }
                        byte[] message = [.. lengthBytes, .. data, .. eomBytes];
                        await socket.SendAsync(message);
                    }
                }
            }
            catch
            {
                if (Protocol == Protocol.Tcp)
                    semaphore.Release();
            }
        }

        /// <summary>
        /// Default Dispose method
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes of the managed and unmanaged resources
        /// </summary>
        /// <param name="disposing">If the manages resources should be disposed</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                listenSocket.Dispose();

                if (connectedSockets != null)
                {
                    foreach (Socket socket in connectedSockets)
                    {
                        socket.Dispose();
                    }
                }
            }
        }
    }
}
