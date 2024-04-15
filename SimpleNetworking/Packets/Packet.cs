using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleNetworking.Packets
{
    /// <summary>
    /// The base class containing static methods to get the type and guid of a packet.
    /// </summary>
    public sealed class Packet : Packet<Packet>
    {
        /// <summary>
        /// Returns the type of a packet from a json string.
        /// </summary>
        /// <param name="json">The json string to get the type from.</param>
        /// <param name="options">Optional JsonSerializer options</param>
        /// <returns>The type of the packet as a string.</returns>
        /// <exception cref="ArgumentException">Thrown if the deserialization fails.</exception>
        public static string GetType(string json, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Deserialize<JsonObject>(json, options)?["Type"]?.ToString() ?? throw new ArgumentException("Failed to get packet type. Json invalid.");
        }

        /// <summary>
        /// Returns the guid of a packet from a json string.
        /// </summary>
        /// <param name="json"> The json string to get the guid from. </param>
        /// <param name="options">Optional JsonSerializer options</param>
        /// <returns>The guid of the packet as a guid,</returns>
        /// <exception cref="ArgumentException">Thrown if the deserialization fails.</exception>
        public static Guid GetGuid(string json, JsonSerializerOptions? options = null)
        {
            return Guid.Parse(JsonSerializer.Deserialize<JsonObject>(json, options)?["Guid"]?.ToString() ?? throw new ArgumentException("Failed to get packet guid. Json invalid."));
        }
    }

    /// <summary>
    /// The base class for all packets.
    /// </summary>
    /// <typeparam name="T">The extending class</typeparam>
    public abstract class Packet<T> where T : Packet<T>
    {
        /// <summary>
        /// Gets the Type of the Packet
        /// </summary>
        public string Type { get; set; } = typeof(T).Name;
        /// <summary>
        /// Gets the Guid of the Packet
        /// </summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Serializes the packet to a json string.
        /// </summary>
        /// <returns>The packet as a json string</returns>
        public string Serialize()
        {
            return JsonSerializer.Serialize(this, typeof(T));
        }

        /// <summary>
        /// Deserializes a json string to a packet.
        /// </summary>
        /// <param name="json">The json string to deserialize</param>
        /// <returns>The object from the json string as <c>T</c></returns>
        /// <exception cref="Exception"></exception>
        public static T Deserialize(string json)
        {
            return JsonSerializer.Deserialize<T>(json) ?? throw new ArgumentException("Failed to deserialize packet. Json invalid.");
        }

        /// <summary>
        /// Trys to deserialize a json string to a packet.
        /// </summary>
        /// <param name="json">The json string to deserialize</param>
        /// <param name="packet">The deserialized packet</param>
        /// <returns>true if it could be deserialized, otherwise false</returns>
        public static bool TryDeserialize(string json, out T? packet)
        {

            try
            {
                packet = Deserialize(json);
                return true;
            }
            catch
            {
                packet = null;
                return false;
            }
        }
    }
}
