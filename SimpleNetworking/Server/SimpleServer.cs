using SimpleNetworking.EventArgs;
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
        private readonly byte[] eomBytes = Encoding.UTF8.GetBytes("<EOM>");
        private readonly Socket listenSocket; private readonly int maxConnections;
        private byte sendSequenceNumber = 1;
        private readonly IPEndPoint localEndPoint;

        private readonly List<Socket>? connectedSockets;
        //TODO: refactor code to use a array of IPEndPoint instead of a list
        private readonly List<IPEndPoint>? udpRemoteEndPoints;
        private Socket GetTcpSocketByEndPoint(IPEndPoint iPEnd)
        {
            if (Protocol != Protocol.Tcp)
                throw new InvalidOperationException("This method is only available for TCP servers.");

            return connectedSockets!.Where(socket => socket.Connected == true).First(s => s.RemoteEndPoint == iPEnd);
        }

        /// <summary>
        /// Gets the protocol used by the server
        /// </summary>
        public Protocol Protocol { get; }

        /// <summary>
        /// Gets if the server at the specified index is connected
        /// </summary>
        /// <param name="index">The index of the client to check</param>
        /// <returns>true if connected, otherwise false</returns>
        public bool IsTcpConnected(int index) => Protocol == Protocol.Tcp && connectedSockets![index].Connected;
        /// <summary>
        /// Gets if the server with the specified remote endpoint is connected
        /// </summary>
        /// <param name="remoteEndPoint">The remote endpoint to check for</param>
        /// <returns>true if the client is connected, otherwise false</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public bool IsTcpConnected(IPEndPoint remoteEndPoint)
        {
            //TODO: Refactor to work like IsTcpConnected(int index).
            if (Protocol != Protocol.Tcp)
                throw new InvalidOperationException("This method is only available for TCP servers.");

            return connectedSockets!.Exists(socket => socket.Connected && socket.RemoteEndPoint == remoteEndPoint);
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
            localEndPoint = new IPEndPoint(ip ?? IPAddress.Any, port);

            switch (protocol)
            {
                case Protocol.Tcp:
                    listenSocket = new Socket(ip?.AddressFamily ?? AddressFamily.Unspecified, SocketType.Stream, ProtocolType.Tcp);
                    connectedSockets = [];
                    break;
                case Protocol.Udp:
                    udpRemoteEndPoints = [];
                    listenSocket = new Socket(ip?.AddressFamily ?? AddressFamily.Unspecified, SocketType.Dgram, ProtocolType.Udp);
                    break;
                default:
                    throw new ArgumentException("Invalid protocol");
            }

            listenSocket.Bind(localEndPoint);
        }

        /// <summary>
        /// Starts listening for incoming connections and messages
        /// </summary>
        /// <param name="token">Token to cancel listening with</param>
        /// <exception cref="ArgumentException"></exception>
        public void StartListen(CancellationToken token)
        {
            _ = Protocol switch
            {
                Protocol.Tcp => ConnectToClients(token),
                Protocol.Udp => ListenUdp(token),
                _ => throw new ArgumentException("Invalid protocol"),
            };
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
            if (Protocol == Protocol.Udp)
            {
                byte[] messageWithEom = [sendSequenceNumber++, .. message, .. eomBytes];
                listenSocket.SendTo(messageWithEom, remoteEndPoint);
            }
            else if (Protocol == Protocol.Tcp)
            {
                GetTcpSocketByEndPoint(remoteEndPoint).Send([.. message, .. eomBytes]);
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
            switch (Protocol)
            {
                case Protocol.Tcp:
                    message = [.. message, .. eomBytes];
                    await GetTcpSocketByEndPoint(remoteEndPoint).SendAsync(message);
                    break;
                case Protocol.Udp:
                    message = [sendSequenceNumber++, .. message, .. eomBytes];
                    await listenSocket.SendToAsync(message, remoteEndPoint);
                    break;
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
            if (Protocol == Protocol.Udp)
            {
                byte[] messageWithEom = [sendSequenceNumber++, .. message, .. eomBytes];
                foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                {
                    listenSocket.SendTo(messageWithEom, remoteEndPoint);
                }
            }
            else if (Protocol == Protocol.Tcp)
            {
                foreach (Socket socket in connectedSockets!)
                {
                    socket.Send([.. message, .. eomBytes]);
                }
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
            switch (Protocol)
            {
                case Protocol.Udp:
                    message = [sendSequenceNumber++, .. message, .. eomBytes];
                    foreach (IPEndPoint remoteEndPoint in udpRemoteEndPoints!)
                    {
                        await listenSocket.SendToAsync(message, remoteEndPoint);
                    }
                    break;
                case Protocol.Tcp:
                    message = [.. message, .. eomBytes];
                    foreach (Socket socket in connectedSockets!)
                    {
                        await socket.SendAsync(message);
                    }
                    break;
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
            if (Protocol == Protocol.Udp)
            {
                byte[] messageWithEom = [sendSequenceNumber++, .. data, .. eomBytes];
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
                    if (!Array.Exists(ipendpoints, ep => ep.Address == (socket.RemoteEndPoint as IPEndPoint)!.Address))
                    {
                        socket.Send([.. data, .. eomBytes]);
                    }
                }
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
            if (Protocol == Protocol.Udp)
            {
                byte[] messageWithEom = [sendSequenceNumber++, .. data, .. eomBytes];
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
                    if (!Array.Exists(ipendpoints, ep => ep.Address == (socket.RemoteEndPoint as IPEndPoint)!.Address))
                    {
                        byte[] message = [.. data, .. eomBytes];
                        await socket.SendAsync(message);
                    }
                }
            }
        }

        private async Task ConnectToClients(CancellationToken token)
        {
            listenSocket.Listen(maxConnections);

            while (!token.IsCancellationRequested)
            {
                Socket socket = await listenSocket.AcceptAsync(token);
                connectedSockets!.Add(socket);
                OnClientConnected?.Invoke(new ClientConnectedEventArgs(socket, DateTime.Now));

                _ = Task.Run(() => ListenTcp(socket, token), token);
            }
        }

        private async Task ListenTcp(Socket socket, CancellationToken token)
        {
            List<byte> truncationBuffer = [];

            while (!token.IsCancellationRequested)
            {
                byte[] receiveBuffer = new byte[socket.ReceiveBufferSize];
                int received = await socket.ReceiveAsync(receiveBuffer, SocketFlags.None, token);

                if (received == 0)
                    break;

                receiveBuffer = receiveBuffer[..received];

                if (truncationBuffer.Count > 0)
                {
                    receiveBuffer = [.. truncationBuffer, .. receiveBuffer];
                    truncationBuffer.Clear();
                }

                Memory<byte> buffer = receiveBuffer.AsMemory(0, receiveBuffer.Length);

                int eomIndex;
                while ((eomIndex = buffer.Span.IndexOf(eomBytes)) >= 0)
                {
                    Memory<byte> message = buffer[..eomIndex];

                    // Invoke message received event
                    OnMessageReceived?.Invoke(new MessageReceivedEventArgs(message.ToArray(), (IPEndPoint)socket.RemoteEndPoint!));
                    buffer = buffer[(eomIndex + eomBytes.Length)..];
                }

                if (!buffer.IsEmpty)
                {
                    truncationBuffer.AddRange(buffer.ToArray());
                    Debug.WriteLine($"Truncated on server: {Encoding.UTF8.GetString(truncationBuffer.ToArray())}.");
                }
            }

            OnClientDisconnected?.Invoke(new ClientDisconnectedEventArgs(socket, DateTime.Now, "Cancellation requested"));
        }

        private async Task ListenUdp(CancellationToken token)
        {
            List<byte> truncationBuffer = [];
            byte lastSequenceNumber = 0;

            while (!token.IsCancellationRequested)
            {
                byte[] receiveBuffer = new byte[listenSocket.ReceiveBufferSize];
                SocketReceiveFromResult result = await listenSocket.ReceiveFromAsync(receiveBuffer, SocketFlags.None, localEndPoint, token);
                int received = result.ReceivedBytes;

                if (result.ReceivedBytes == 0)
                    break;

                if (!udpRemoteEndPoints!.Contains((IPEndPoint)result.RemoteEndPoint))
                {
                    udpRemoteEndPoints.Add((IPEndPoint)result.RemoteEndPoint);
                    OnClientConnected?.Invoke(new ClientConnectedEventArgs(listenSocket, DateTime.Now));
                }

                receiveBuffer = receiveBuffer[..received];

                if (truncationBuffer.Count > 0)
                {
                    receiveBuffer = [.. truncationBuffer, .. receiveBuffer];
                    truncationBuffer.Clear();
                }

                Memory<byte> buffer = receiveBuffer.AsMemory(0, receiveBuffer.Length);

                int eomIndex;
                while ((eomIndex = buffer.Span.IndexOf(eomBytes)) >= 0)
                {
                    Memory<byte> message = buffer[..eomIndex];

                    byte sequenceNumber = message.Span[0];
                    message = message[1..];

                    if (sequenceNumber != lastSequenceNumber + 1
                        && !(sequenceNumber == 0 && lastSequenceNumber == 255))
                    {
                        Debug.WriteLine($"Packet loss detected. Client skipping packet {sequenceNumber}. " +
                            $"Waiting for packet: {lastSequenceNumber + 1}");

                        buffer = buffer[(eomIndex + eomBytes.Length)..];

                        if (sequenceNumber > lastSequenceNumber)
                            lastSequenceNumber = sequenceNumber;

                        continue;
                    }

                    lastSequenceNumber = sequenceNumber;

                    // Invoke message received event
                    OnMessageReceived?.Invoke(new MessageReceivedEventArgs(message.ToArray(), (IPEndPoint)result.RemoteEndPoint));
                    buffer = buffer[(eomIndex + eomBytes.Length)..];
                }

                // Remaining bytes are part of the next message or are truncated
                if (!buffer.IsEmpty)
                {
                    truncationBuffer.AddRange(buffer.ToArray());
                    Debug.WriteLine($"Truncated on server: {Encoding.UTF8.GetString(truncationBuffer.ToArray())}.");
                }
            }

            //TODO: Give proper reason for stopping
            OnClientDisconnected?.Invoke(new ClientDisconnectedEventArgs(listenSocket, DateTime.Now, "Something went wrong"));
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
