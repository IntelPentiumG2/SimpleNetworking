using SimpleNetworking.EventArgs;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Diagnostics;

namespace SimpleNetworking.Client
{
    /// <summary>
    /// A simple client to connect to a host using Tcp or Udp.
    /// </summary>
    [Obsolete("This class was as prototype. Use SimpleClient instead.")]
    public class SimpleClientBeta : IDisposable
    {
        private readonly TcpClient? tcpClient;
        private readonly UdpClient? udpClient;

        /// <summary>
        /// Gets the status of the connection. True if the client is connected to a host. Otherwise false.
        /// </summary>
        public bool IsConnected => (Protocol == Protocol.Tcp) ? tcpClient?.Connected != false : udpClient?.Client.Connected != false;
        /// <summary>
        /// Gets the socket used for the connection. Null if the client is not connected.
        /// </summary>
        private Socket? Socket => (Protocol == Protocol.Tcp) ? tcpClient?.Client : udpClient?.Client;
        /// <summary>
        /// Gets the protocol used for the connection.
        /// </summary>
        public Protocol Protocol { get; }
        /// <summary>
        /// Gets the remote endpoint of the connection.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Fires when a message is received from the host.
        /// </summary>
        public event Action<MessageReceivedEventArgs>? OnMessageReceived;
        /// <summary>
        /// Fires when the client successfully connects to the host.
        /// </summary>
        public event Action<ClientConnectedEventArgs>? OnConnectionOpened;
        /// <summary>
        /// Fires when the client is disconnected from the host.
        /// </summary>
        public event Action<ClientDisconnectedEventArgs>? OnConnectionClosed;

        private const string eom = "<EOM>";
        private readonly byte[] eomBytes;
        private byte udpSendSequenceNumber = 0;

        /// <summary>
        /// Creates a new instance of the SimpleClientBeta class.
        /// </summary>
        /// <param name="protocol">The protocol to use for the connection</param>
        /// <param name="port">The port to connect on</param>
        /// <param name="ip">The Ip to connect to</param>
        /// <exception cref="ArgumentException">Throws if an invalid protocol was given</exception>
        public SimpleClientBeta(Protocol protocol, int port, IPAddress? ip = null)
        {
            this.Protocol = protocol;
            RemoteEndPoint = new IPEndPoint(ip ?? IPAddress.Any, port);
            eomBytes = Encoding.UTF8.GetBytes(eom);

            if (protocol == Protocol.Tcp)
            {
                tcpClient = new TcpClient();
            }
            else if (protocol == Protocol.Udp)
            {
                udpClient = new UdpClient();
            }
            else
            {
                throw new ArgumentException("Invalid protocol");
            }
        }

        /// <summary>
        /// Creates a new instance of the SimpleClientBeta class.
        /// </summary>
        /// <param name="protocol">The protocol to use for the connection</param>
        /// <param name="remoteEndPoint">The IPEndPoint to connect to</param>
        /// <exception cref="ArgumentException">Throws if an invalid protocol was given</exception>
        public SimpleClientBeta(Protocol protocol, IPEndPoint remoteEndPoint)
        {
            this.Protocol = protocol;
            RemoteEndPoint = remoteEndPoint;
            eomBytes = Encoding.UTF8.GetBytes(eom);

            if (protocol == Protocol.Tcp)
            {
                tcpClient = new TcpClient();
            }
            else if (protocol == Protocol.Udp)
            {
                udpClient = new UdpClient();
            }
            else
            {
                throw new ArgumentException("Invalid protocol");
            }
        }

        /// <summary>
        /// Creates a new instance of the SimpleClientBeta class.
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="port">The port to connect on</param>
        /// <param name="ip">The ip to connect to</param>
        /// <exception cref="FormatException">Throws if the ip cant be parsed</exception>
        public SimpleClientBeta(Protocol protocol, int port, string ip) : this(protocol, port, IPAddress.Parse(ip)) { }


        /// <summary>
        /// Connects the client to the host.
        /// </summary>
        /// <param name="token">The CancellationToken to cancel the background listening for messages.</param>
        public void Connect(CancellationToken token)
        {
            if (Protocol == Protocol.Tcp)
            {
                tcpClient!.Connect(RemoteEndPoint);
                Task.Run(() => StartListeningBytes(token), token);
            }
            else if (Protocol == Protocol.Udp)
            {
                udpClient!.Connect(RemoteEndPoint);
                Task.Run(() => StartListeningBytes(token), token);
            }

            OnConnectionOpened?.Invoke(new ClientConnectedEventArgs(this.Socket!, DateTime.Now));
        }

        /// <summary>
        /// Dissconnects the client from the host.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Disconnect()
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");

            if (Protocol == Protocol.Tcp)
            {
                tcpClient!.Close();
            }
            else if (Protocol == Protocol.Udp)
            {
                udpClient!.Close();
            }

            OnConnectionClosed?.Invoke(new ClientDisconnectedEventArgs(this.Socket, DateTime.Now, "me no know"));
        }

        // TODO: Add length to the message after the sequence number to ease truncation checks
        /// <summary>
        /// Sends a message to the connected host.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Send(string message)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");
            byte[] data;

            switch (Protocol)
            {
                case Protocol.Tcp:
                    data = Encoding.UTF8.GetBytes(message.Trim() + eom);
                    tcpClient!.GetStream().Write(data);
                    break;
                case Protocol.Udp:
                    data = [udpSendSequenceNumber++, .. Encoding.UTF8.GetBytes(message.Trim()), .. eomBytes];
                    udpClient!.Send(data, data.Length);
                    break;
            }
        }

        /// <summary>
        /// Sends a byte array to the connected host.
        /// </summary>
        /// <param name="bytes">The byte array to send</param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Send(byte[] bytes)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");

            switch (Protocol)
            {
                case Protocol.Tcp:
                    bytes = [.. bytes, .. eomBytes];
                    tcpClient!.GetStream().Write(bytes);
                    break;
                case Protocol.Udp:
                    bytes = [udpSendSequenceNumber++, .. bytes, .. eomBytes];
                    udpClient!.Send(bytes, bytes.Length);
                    break;
            }
        }

        /// <summary>
        /// Sends a message to the connected host.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <returns>A task waiting for the message to be send</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task SendAsync(string message)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");
            byte[] data;

            switch (Protocol)
            {
                case Protocol.Tcp:
                    data = Encoding.UTF8.GetBytes(message.Trim() + eom);
                    await tcpClient!.GetStream().WriteAsync(data);
                    break;
                case Protocol.Udp:
                    data = [udpSendSequenceNumber++, .. Encoding.UTF8.GetBytes(message.Trim()), .. eomBytes];
                    await udpClient!.SendAsync(data, data.Length);
                    break;
            }
        }

        /// <summary>
        /// Sends a byte array to the connected host.
        /// </summary>
        /// <param name="bytes">The byte array to send</param>
        /// <returns>A task waiting for the message to be send</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task SendAsync(byte[] bytes)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");

            switch (Protocol)
            {
                case Protocol.Tcp:
                    bytes = [.. bytes, .. eomBytes];
                    await tcpClient!.GetStream().WriteAsync(bytes);
                    break;
                case Protocol.Udp:
                    bytes = [udpSendSequenceNumber++, .. bytes, .. eomBytes];
                    await udpClient!.SendAsync(bytes, bytes.Length);
                    break;
            }
        }

        /// <summary>
        /// Starts listening for incoming messages.
        /// </summary>
        /// <param name="token">A CancellationToken to cancel the listening cycle</param>
        /// <returns>A task cuntinously listening for messages</returns>
        /// <exception cref="InvalidOperationException">Throws if an invalid protocol is given </exception>
        private async Task StartListeningBytes(CancellationToken token)
        {
            List<byte> pendingBytes = [];
            byte lastSequenceNumber = 0;

            try
            {
                while (IsConnected)
                {
                    byte[] buffer = new byte[1024];

                    switch (Protocol)
                    {
                        case Protocol.Tcp:
                            int bytesRead = await tcpClient!.GetStream().ReadAsync(buffer, token);
                            buffer = buffer[..bytesRead];
                        break;
                        case Protocol.Udp:
                            buffer = (await udpClient!.ReceiveAsync(token)).Buffer;
                        break;
                        default:
                            throw new InvalidOperationException("Invalid protocol");
                    }

                    if (buffer.Search(eomBytes) == -1)
                    {
                        pendingBytes.AddRange(buffer);
                        continue;
                    }

                    if (pendingBytes.Count > 0)
                    {
                        buffer = [.. pendingBytes, .. buffer];
                        pendingBytes.Clear();
                    }

                    int eomIndex = buffer.Search(eomBytes);
                    while (eomIndex > -1)
                    {
                        byte[] message = buffer[..eomIndex];
                        // remove the message from the buffer
                        buffer = buffer[(eomIndex + eom.Length)..];

                        if (Protocol == Protocol.Udp)
                        {
                            byte sequenceNumber = message[0];
                            message = message[1..];

                            if (sequenceNumber != lastSequenceNumber + 1
                                && sequenceNumber != 0
                                && (lastSequenceNumber != 255 && sequenceNumber == 0))
                            {
                                Debug.WriteLine($"Packet loss detected. Skipping packet {sequenceNumber}. " +
                                    $"Waiting for packet: {lastSequenceNumber + 1}");
                                lastSequenceNumber = sequenceNumber;
                                continue;
                            }

                            lastSequenceNumber = sequenceNumber;
                        }

                        eomIndex = buffer.Search(eomBytes);
                        OnMessageReceived?.Invoke(new MessageReceivedEventArgs(message, RemoteEndPoint));
                    }

                    if (buffer.Length > 0)
                    {
                        pendingBytes.AddRange(buffer);
                        Debug.WriteLine("Truncated: " + Encoding.UTF8.GetString(pendingBytes.ToArray()));
                    }
                }
            }
            catch (Exception ex)
            {
                OnConnectionClosed?.Invoke(new ClientDisconnectedEventArgs(this.Socket, DateTime.Now, ex.Message));
                Debug.WriteLine($"Error in StartListeningBytes: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Disposes the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the client.
        /// </summary>
        /// <param name="disposing">Sets wether the clients should be disposed</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Protocol == Protocol.Tcp)
                {
                    tcpClient?.Dispose();
                }
                else if (Protocol == Protocol.Udp)
                {
                    udpClient?.Dispose();
                }
            }
        }
    }
}
