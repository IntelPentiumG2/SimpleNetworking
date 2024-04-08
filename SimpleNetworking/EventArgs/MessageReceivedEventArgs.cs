using System.Net;
using System.Text;

namespace SimpleNetworking.EventArgs
{
    public class MessageReceivedEventArgs(byte[] buffer, IPEndPoint remoteEp)
    {
        public byte[] Buffer { get; } = buffer;
        public IPEndPoint Sender { get; } = remoteEp;
        public string MessageString => Encoding.UTF8.GetString(Buffer);
    }
}
