using SimpleNetworking.EventArgs;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SimpleNetworking.Client
{
    /// <summary>
    /// A simple client to connect to a host using Tcp or Udp.
    /// </summary>
    public class SimpleClient : IDisposable
    {
        private readonly Socket client;
        private byte sendingSequenceNumber = 1;
        private const string eom = "<EOM>";
        private readonly byte[] eomBytes = Encoding.UTF8.GetBytes(eom);
        private int EomLength => eomBytes.Length;
        private readonly int prefixLength;

        /// <summary>
        /// Gets if the handshake was successful.
        /// </summary>
        public bool HandshakeSuccessful { get; private set; } = false;
        /// <summary>
        /// Gets if the client is connected to a host.
        /// </summary>
        public bool IsConnected => client.Connected;
        /// <summary>
        /// Gets the protocol used by the client.
        /// </summary>
        public Protocol Protocol { get; }
        /// <summary>
        /// Gets the remote endpoint the client is connected to.
        /// </summary>
        public IPEndPoint RemoteEndPoint { get; }
        /// <summary>
        /// Gets the local endpoint of the client.
        /// </summary>
        public IPEndPoint? LocalEndPoint { get; private set; }
        /// <summary>
        /// Gets the buffer size for reading messages.
        /// </summary>
        public int ReadBufferSize { get; private set; } = 16384;
        /// <summary>
        /// Gets the buffer size for sending messages.
        /// </summary>
        public int SendBufferSize { get; private set; } = 8192;

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

        /// <summary>
        /// Creates a new SimpleClient with the specified protocol, port and ip.
        /// Watch out for ReadBufferSize and SendBufferSize, they are set to 16384 and 8192 by default.
        /// Trying to send or receive a message larger than the buffer size will throw an exception.
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="port">The port to connect on</param>
        /// <param name="ip">The ip to connect to</param>
        /// <exception cref="ArgumentException"></exception>
        public SimpleClient(Protocol protocol, int port, IPAddress? ip = null)
        {
            this.Protocol = protocol;
            RemoteEndPoint = new IPEndPoint(ip ?? IPAddress.Any, port);

            client = protocol switch
            {
                Protocol.Tcp => new Socket(RemoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp),
                Protocol.Udp => new Socket(RemoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp),
                _ => throw new ArgumentException("Invalid protocol"),
            };

            prefixLength = Global.PrefixLength(Protocol);
            client.SendBufferSize = SendBufferSize;
            client.ReceiveBufferSize = ReadBufferSize;
            LocalEndPoint = client.LocalEndPoint as IPEndPoint;
        }

        /// <summary>
        /// Creates a new SimpleClient with the specified protocol, port and ip.
        /// Watch out for ReadBufferSize and SendBufferSize, they are set to 16384 and 8192 by default.
        /// Trying to send or receive a message larger than the buffer size will throw an exception.
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="port">The port to connect to</param>
        /// <param name="ip">The ip to connect to</param>
        /// <exception cref="ArgumentException">Throws if an invalid protocol was given</exception>
        /// <exception cref="FormatException">Throws if the ip is not valid</exception>
        public SimpleClient(Protocol protocol, int port, string ip) : this(protocol, port, IPAddress.Parse(ip)) { }

        /// <summary>
        /// Creates a new SimpleClient with the specified protocol, port and ip.
        /// Watch out for ReadBufferSize and SendBufferSize, they are set to 16384 and 8192 by default.
        /// Trying to send or receive a message larger than the buffer size will throw an exception.
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="remoteEndPoint">The remote endpoint to connect to</param>
        /// <exception cref="ArgumentException">Throws if an invalid protocol was given</exception>
        public SimpleClient(Protocol protocol, IPEndPoint remoteEndPoint) : this(protocol, remoteEndPoint.Port, remoteEndPoint.Address) { }

        /// <summary>
        /// Sets the buffer sizes for the client.
        /// Default values are 16384 for ReadBufferSize and 8192 for SendBufferSize.
        /// </summary>
        /// <param name="readBufferSize">The size for the ReceiveBufferSize property</param>
        /// <param name="sendBufferSize">The size for the SendBufferSize</param>
        public void SetBufferSizes(int readBufferSize, int sendBufferSize)
        {
            ReadBufferSize = readBufferSize;
            SendBufferSize = sendBufferSize;
            client.SendBufferSize = sendBufferSize;
            client.ReceiveBufferSize = readBufferSize;
        }

        /// <summary>
        /// Connects the client to the host and starts listening for messages.
        /// </summary>
        /// <param name="token">Cancellation token to cancel the listening loop with</param>
        public void Connect(CancellationToken token)
        {
            client.Connect(RemoteEndPoint);
            LocalEndPoint = client.LocalEndPoint as IPEndPoint;
            if (Protocol == Protocol.Udp)
            {
                HandshakeSuccessful = true;
                OnConnectionOpened?.Invoke(new ClientConnectedEventArgs(client, DateTime.Now));
            }
            _ = StartListeningBytes(token);
        }

        /// <summary>
        /// Disconnects the client from the host.
        /// </summary>
        public void Disconnect()
        {
            if (IsConnected && Protocol == Protocol.Tcp)
                client.Disconnect(false);
        }

       /// <summary>
       /// Sends a message to the connected host.
       /// </summary>
       /// <param name="message">The message to send as string</param>
       /// <exception cref="InvalidOperationException">Throws if the client is not connected to a host</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Send(string message) => Send(Encoding.UTF8.GetBytes(message.Trim()));

        /// <summary>
        /// Sends a message to the connected host.
        /// </summary>
        /// <param name="data">The message to send as a byte array</param>
        /// <exception cref="InvalidOperationException">Throws if the client is not connected to a host</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Send(byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");
            if (!HandshakeSuccessful) return;

            byte[] lengthBytes = BitConverter.GetBytes(((ushort)data.Length));
            data = Protocol switch
            {
                Protocol.Tcp => [.. lengthBytes, .. data, .. eomBytes],
                Protocol.Udp => [.. lengthBytes, sendingSequenceNumber++, .. data, .. eomBytes],
                _ => throw new InvalidOperationException("Invalid protocol"),
            };
            if (data.Length > SendBufferSize)
                throw new ArgumentOutOfRangeException(nameof(data), $"Message is too large to send in one packet. {data.Length - SendBufferSize} bytes over limit.");
            client.Send(data);
        }

        /// <summary>
        /// Sends a message to the connected host asynchronously.
        /// </summary>
        /// <param name="message">The message to send as a string</param>
        /// <returns>A task sending the message</returns>
        /// <exception cref="InvalidOperationException">Throws if the client is not connected to a host</exception>
        public async Task SendAsync(string message) => await SendAsync(Encoding.UTF8.GetBytes(message.Trim()));

        /// <summary>
        /// Sends a message to the connected host asynchronously.
        /// </summary>
        /// <param name="data">The message to send as a byte array</param>
        /// <returns>A task sending the message</returns>
        /// <exception cref="InvalidOperationException">Throws if the client is not connected to a host</exception>
        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");
            if (!HandshakeSuccessful) return;

            byte[] lengthBytes = BitConverter.GetBytes(((ushort)data.Length));
            data = Protocol switch
            {
                Protocol.Tcp => [.. lengthBytes, .. data, .. eomBytes],
                Protocol.Udp => [.. lengthBytes, sendingSequenceNumber++, .. data, .. eomBytes],
                _ => throw new InvalidOperationException("Invalid protocol"),
            };
            if (data.Length > SendBufferSize)
                throw new ArgumentOutOfRangeException(nameof(data), $"Message is too large to send in one packet. {data.Length - SendBufferSize} bytes over limit.");
            await client.SendAsync(data);
        }

        /// <summary>
        /// Optimized? version of StartListeningBytes.
        /// Listens for messages from the host and fires the OnMessageReceived event when a message is received.
        /// </summary>
        /// <param name="token">A token to cancel listening with</param>
        /// <returns>A task listening for messages</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task StartListeningBytes(CancellationToken token)
        {
            if (!IsConnected) throw new InvalidOperationException("Client is not connected to a host.");

            List<byte> truncationBuffer = [];
            byte lastSequenceNumber = 0;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] receiveBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                    int received = await client.ReceiveAsync(receiveBuffer, token);

                    if (received == 0)
                    {
                        ArrayPool<byte>.Shared.Return(receiveBuffer);
                        break;
                    }

                    if (!HandshakeSuccessful && Protocol == Protocol.Tcp)
                    {
                        HandshakeSuccessful = true;
                        OnConnectionOpened?.Invoke(new ClientConnectedEventArgs(client, DateTime.Now));
                        ArrayPool<byte>.Shared.Return(receiveBuffer);
                        continue;
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
                        if (messageLength > ReadBufferSize)
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

                        if (Protocol == Protocol.Udp)
                        {
                            byte sequenceNumber = buffer.Span[2];

                            if (sequenceNumber != lastSequenceNumber + 1
                                && !(sequenceNumber == 0 && lastSequenceNumber == 255))
                            {
                                Debug.WriteLine($"Packet loss detected. Client skipping packet {sequenceNumber}. " +
                                    $"Waiting for packet: {lastSequenceNumber + 1}");

                                buffer = buffer[(messageLength + eomBytes.Length + prefixLength)..];

                                continue;
                            }

                            lastSequenceNumber = sequenceNumber;
                        }

                        // Invoke message received event
                        OnMessageReceived?.Invoke(new MessageReceivedEventArgs(message.ToArray(), (IPEndPoint)client.RemoteEndPoint!));
                        buffer = buffer[(messageLength + eomBytes.Length + prefixLength)..];
                    }

                    // Remaining bytes are part of the next message or are truncated
                    if (!buffer.IsEmpty)
                    {
                        truncationBuffer.AddRange(buffer.ToArray());
                        Debug.WriteLine($"Truncated on client: {Encoding.UTF8.GetString(truncationBuffer.ToArray())}.");
                    }

                    ArrayPool<byte>.Shared.Return(receiveBuffer);
                }
            }
            catch (OperationCanceledException)
            {
                OnConnectionClosed?.Invoke(new ClientDisconnectedEventArgs(client, DateTime.Now, "Connection closed by client."));
            }
            catch (Exception ex)
            {
                OnConnectionClosed?.Invoke(new ClientDisconnectedEventArgs(client, DateTime.Now, ex.Message + "|" + ex.StackTrace));
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
        /// <param name="disposing">If the client should be disposed of</param>
        protected virtual void Dispose(bool disposing)
        {
            client.Dispose();
        }
    }
}
