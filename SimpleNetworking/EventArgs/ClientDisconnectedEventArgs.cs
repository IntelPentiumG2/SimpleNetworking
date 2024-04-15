using System.Net.Sockets;

namespace SimpleNetworking.EventArgs
{
    /// <summary>
    /// Event arguments for when a client disconnects
    /// </summary>
    /// <param name="socket">The socket that disconnected</param>
    /// <param name="disconnectionTime">The time the client disconnected</param>
    /// <param name="reason">The reason for the disconnect</param>
    public class ClientDisconnectedEventArgs(Socket? socket, DateTime disconnectionTime, string reason)
    {
        /// <summary>
        /// The socket that disconnected
        /// </summary>
        public Socket? Socket { get; set; } = socket;
        /// <summary>
        /// The time the client disconnected
        /// </summary>
        public DateTime DisconnectionTime { get; set; } = disconnectionTime;
        /// <summary>
        /// The reason for the disconnect
        /// </summary>
        public string Reason { get; set; } = reason;
    }
}
