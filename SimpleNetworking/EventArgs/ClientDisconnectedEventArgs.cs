using System.Net.Sockets;

namespace SimpleNetworking.EventArgs
{
    public class ClientDisconnectedEventArgs(Socket? socket, DateTime disconnectionTime, string reason)
    {
        public Socket? Socket { get; set; } = socket;
        public DateTime DisconnectionTime { get; set; } = disconnectionTime;
        public string Reason { get; set; } = reason;
    }
}
