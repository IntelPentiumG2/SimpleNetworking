using SimpleNetworking.EventArgs;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SimpleNetworking.Server
{
    [Experimental("MaxInefficiency")]
    public class SimpleServerBeta : IDisposable
    {
        private readonly TcpListener[]? tcpListeners;
        private readonly TcpClient[]? tcpClients;
        private readonly List<IPEndPoint>? udpEndPoints;
        private readonly UdpClient? udpClient;
        private readonly Protocol protocol;
        private byte sendingSequenceNumber = 1;
        private const string eom = "<EOM>";
        readonly byte[] eomBytes = Encoding.UTF8.GetBytes(eom);

        public event Action<MessageReceivedEventArgs>? OnMessageReceived;
        public event Action<ClientConnectedEventArgs>? OnConnectionOpened;
        public event Action<ClientDisconnectedEventArgs>? OnConnectionClosed;

        /// <summary>
        /// Creates a new server with the specified protocol, port, max clients, ip, and dualmode.
        /// </summary>
        /// <param name="protocol">The protocol to use</param>
        /// <param name="port">The port to listen on</param>
        /// <param name="ip">The ip to listen on</param>
        /// <param name="maxClients">The max amount of clients allowed to connect</param>
        /// <param name="dualmode">If the clients should use dualmode</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public SimpleServerBeta(Protocol protocol, int port, IPAddress? ip = null, int maxClients = 1, bool dualmode = true)
        {
            this.protocol = protocol;

            switch (protocol)
            {
                case Protocol.Tcp:
                    tcpListeners = new TcpListener[maxClients];
                    tcpClients = new TcpClient[maxClients];
                    for (int i = 0; i < maxClients; i++)
                    {
                        tcpListeners[i] = new TcpListener(ip ?? IPAddress.Any, port);
                    }
                    break;
                case Protocol.Udp:
                    udpEndPoints = [];
                    IPEndPoint endPoint = new(ip ?? IPAddress.Any, port);
                    udpClient = new UdpClient(endPoint);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null);
            }
        }

        public void StartListen(CancellationToken token)
        {
            switch (protocol)
            {
                case Protocol.Tcp:
                    _ = ListenTcp(token);
                    break;
                case Protocol.Udp:
                    ListenUdp(token);
                    break;
            }
        }

        private async Task ListenTcp(CancellationToken token)
        {
            try
            {
                for (int i = 0; i < tcpListeners!.Length; i++)
                {
                    tcpListeners[i].Start();
                    tcpClients![i] = await tcpListeners[i].AcceptTcpClientAsync(token);
                    tcpListeners[i].Stop();

                    await Task.Run(async () =>
                    {
                        List<byte> truncationBuffer = [];

                        while (!token.IsCancellationRequested && tcpClients[i].GetStream().CanRead)
                        {
                            Memory<byte> bytes = new byte[tcpClients[i].ReceiveBufferSize];
                            int bytesRead = await tcpClients[i].GetStream().ReadAsync(bytes, token);

                            if (bytesRead == 0)
                            {
                                break;
                            }

                            Memory<byte> buffer = bytes[..bytesRead];

                            if (truncationBuffer.Count > 0)
                            {
                                buffer = truncationBuffer.Concat(buffer.ToArray()).ToArray().AsMemory();
                                truncationBuffer.Clear();
                            }

                            int eomIndex;
                            while ((eomIndex = buffer.Span.IndexOf(eomBytes)) != -1)
                            {
                                byte[] messageBytes = buffer.Span[..eomIndex].ToArray();

                                OnMessageReceived?.Invoke(new MessageReceivedEventArgs(messageBytes, (IPEndPoint)tcpClients[i].Client.RemoteEndPoint!));
                                buffer = buffer[(eomIndex + eomBytes.Length)..];
                            }

                            if (buffer.Length > 0)
                            {
                                truncationBuffer.AddRange(buffer.ToArray());
                                Debug.WriteLine("Truncation buffer: " + string.Join(", ", truncationBuffer));
                            }
                        }
                    }, token);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message + " - " + ex.StackTrace);
                throw;
            }
        }

        public void SendToAll(byte[] message)
        {
            if (protocol == Protocol.Tcp)
            {
                foreach (var client in tcpClients!)
                {
                    if (client.Connected && client.GetStream().CanWrite)
                    {
                        client.GetStream().Write([..message, .. eomBytes ]);
                    }
                }
            }
            else
            {
                message = [sendingSequenceNumber++, .. message, .. eomBytes];
                
                foreach (var endPoint in udpEndPoints!)
                {
                    udpClient!.Send(message, message.Length, endPoint);
                }
            }
        }

        public void Send(byte[] message, IPEndPoint? endPoint = null)
        {
            if (protocol == Protocol.Tcp)
            {
                foreach (var client in tcpClients!)
                {
                    if (client.Connected)
                    {
                        client.GetStream().Write(message, 0, message.Length);
                    }
                }
            }
            else
            {
                message = [sendingSequenceNumber++, .. message];
                udpClient!.Send(message, message.Length, endPoint ?? udpEndPoints![0]);
            }
        }

        private void ListenUdp(CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                List<byte> truncationBuffer = [];
                byte lastSequenceNumber = 0;

                while (!token.IsCancellationRequested)
                {
                    UdpReceiveResult data = await udpClient!.ReceiveAsync();
                    IPEndPoint remoteEndPoint = data.RemoteEndPoint;

                    if (!udpEndPoints!.Contains(remoteEndPoint))
                    {
                        udpEndPoints.Add(remoteEndPoint);
                        //OnConnectionOpened?.Invoke(new ClientConnectedEventArgs(remoteEndPoint));
                    }

                    byte[] buffer = data.Buffer;

                    if (truncationBuffer.Count > 0)
                    {
                        buffer = [.. truncationBuffer, .. buffer];
                        truncationBuffer.Clear();
                    }

                    int eomIndex;
                    while ((eomIndex = buffer.Search(eomBytes)) != -1)
                    {
                        byte[] messageBytes = buffer[..eomIndex];

                        byte sequenceNumber = messageBytes[0];

                        if (sequenceNumber != lastSequenceNumber + 1
                            && !(sequenceNumber == 0 || lastSequenceNumber == 255))
                        {
                            Debug.WriteLine($"Packet loss detected. Server skipping packet {sequenceNumber}. " +
                                $"Waiting for packet: {lastSequenceNumber + 1}");

                            buffer = buffer[(eomIndex + eomBytes.Length)..];

                            if (sequenceNumber > lastSequenceNumber)
                                lastSequenceNumber = sequenceNumber;

                            continue;
                        }

                        lastSequenceNumber = sequenceNumber;

                        OnMessageReceived?.Invoke(new MessageReceivedEventArgs(messageBytes, remoteEndPoint));
                        buffer = buffer[(eomIndex + eomBytes.Length)..];
                    }

                    if (buffer.Length > 0)
                    {
                        truncationBuffer.AddRange(buffer);
                    }
                }
            }, token);
        }

        public IEnumerable<IPEndPoint> GetClients()
        {
            for (int i = 0; i < tcpClients!.Length; i++)
            {
                if (tcpClients[i].Connected)
                {
                    yield return (IPEndPoint)tcpClients[i].Client.RemoteEndPoint!;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (protocol == Protocol.Tcp)
                {
                    foreach (var listener in tcpListeners!)
                    {
                        listener.Stop();
                        listener.Dispose();
                    }
                }
                else
                {
                    udpClient!.Close();
                    udpClient.Dispose();
                }
            }

            // Dispose unmanaged resources
        }
    }
}
