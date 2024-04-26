namespace SimpleNetworking
{
    /// <summary>
    /// Valid protocol types for the SimpleNetworking library
    /// </summary>
    public enum Protocol
    {
        /// <summary>
        /// TCP protocol
        /// </summary>
        Tcp,
        /// <summary>
        /// UDP protocol
        /// </summary>
        Udp
    }

    /// <summary>
    /// The type of message being sent
    /// </summary>
    public enum MessageType : byte
    {
        /// <summary>
        /// Defines a data message
        /// </summary>
        Data,
        /// <summary>
        /// Defines a handshake message
        /// </summary>
        Handshake,
        /// <summary>
        /// Defines an acknowledgement message
        /// </summary>
        Ack
    }

    /// <summary>
    /// Contains global constants and methods for the SimpleNetworking library
    /// </summary>
    public static class Global
    {
        private const int MESSAGE_DATA_LENGTH_PREFIX_SIZE = 2;
        private const int MESSAGE_TYPE_PREFIX_SIZE = 0;
        private const int SEQUENCE_NUMBER_PREFIX_SIZE = 1;

        /// <summary>
        /// Returns the prefix length for a given protocol
        /// </summary>
        /// <param name="protocol">The Protocol to use</param>
        /// <returns>The length of the prefixes</returns>
        public static int PrefixLength(Protocol protocol) => protocol switch
        {
            Protocol.Tcp => MESSAGE_DATA_LENGTH_PREFIX_SIZE + MESSAGE_TYPE_PREFIX_SIZE,
            Protocol.Udp => MESSAGE_DATA_LENGTH_PREFIX_SIZE + MESSAGE_TYPE_PREFIX_SIZE + SEQUENCE_NUMBER_PREFIX_SIZE,
            _ => throw new InvalidOperationException($"UDP and TCP supported, not {protocol}")
        };
    }
}
