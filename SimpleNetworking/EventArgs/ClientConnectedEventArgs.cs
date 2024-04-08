using System.Net.Sockets;

namespace SimpleNetworking.EventArgs
{
    public class ClientConnectedEventArgs(Socket socket, DateTime connectionTime)
    {
        public Socket Socket { get; set; } = socket;
        public DateTime ConnectionTime { get; set; } = connectionTime;
    }
}
