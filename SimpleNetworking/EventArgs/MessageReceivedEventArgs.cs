using System.Net;
using System.Text;

namespace SimpleNetworking.EventArgs
{
    /// <summary>
    /// Event arguments for when a message is received
    /// </summary>
    /// <param name="buffer">The buffer received</param>
    /// <param name="remoteEp">The remote endpoint it was received from</param>
    public class MessageReceivedEventArgs(byte[] buffer, IPEndPoint remoteEp)
    {
        /// <summary>
        /// The buffer received
        /// </summary>
        public byte[] Buffer { get; } = buffer;
        /// <summary>
        /// The remote endpoint the buffer was received from
        /// </summary>
        public IPEndPoint Sender { get; } = remoteEp;
        /// <summary>
        /// The buffer received as a string
        /// </summary>
        public string MessageString => Encoding.UTF8.GetString(Buffer);
    }
}
