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
}
