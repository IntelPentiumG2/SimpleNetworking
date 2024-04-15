using System.Net.Sockets;

namespace SimpleNetworking.EventArgs
{
    /// <summary>
    /// Contains the socket of the client that connected and the time of the connection
    /// </summary>
    /// <param name="socket">The socket</param>
    /// <param name="connectionTime">The time</param>
    public class ClientConnectedEventArgs(Socket socket, DateTime connectionTime)
    {
        /// <summary>
        /// The socket of the client that connected
        /// </summary>
        public Socket Socket { get; set; } = socket;
        /// <summary>
        /// The time of the connection
        /// </summary>
        public DateTime ConnectionTime { get; set; } = connectionTime;
    }
}
