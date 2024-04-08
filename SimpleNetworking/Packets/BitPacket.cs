using System;
using System.Buffers.Binary;
using System.Data;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace SimpleNetworking.Packets
{
    /// <summary>
    /// This class is in development and is missing a lot of features. <br></br>
    /// A class representing a packet with a type, data, length and guid. <br></br>
    /// It can be serialized and deserialized as a byte array. <br></br>
    /// Its on average 2x faster than the Packet class which is based on System.Text.Json.
    /// </summary>
    public class BitPacket
    {
        public string Type { get; }
        public byte[] Data { get; }
        public int Length { get; }
        public Guid Guid { get; }
        private static readonly Dictionary<string, PropertyInfo[]> reflectionCache = [];

        public BitPacket(string type, byte[] data)
        {
            Type = type;
            Data = data;
            Length = data.Length;
            Guid = Guid.NewGuid();
        }

        public BitPacket(string type, byte[] data, Guid guid)
        {
            Type = type;
            Data = data;
            Length = data.Length;
            Guid = guid;
        }

        public byte[] Serialize()
        {
            int totalLength = 4 + Encoding.UTF8.GetByteCount(Type) + 4 + Length + 16;
            byte[] buffer = new byte[totalLength];
            int offset = 0;

            offset += WriteString(buffer, offset, Type);
            offset += WriteInt32(buffer, offset, Length);
            Buffer.BlockCopy(Data, 0, buffer, offset, Length);
            offset += Length;
            Buffer.BlockCopy(Guid.ToByteArray(), 0, buffer, offset, 16);

            return buffer;
        }

        public static BitPacket Deserialize(byte[] byteData)
        {
            int offset = 0;

            string type = ReadString(byteData, ref offset);
            int length = ReadInt32(byteData, ref offset);
            byte[] data = new byte[length];
            Buffer.BlockCopy(byteData, offset, data, 0, length);
            offset += length;
            Guid guid = new(byteData[offset..(offset + 16)]);

            return new BitPacket(type, data, guid);
        }

        private static readonly MemoryStream MemoryStreamInstance = new();
        private static readonly BinaryWriter BinaryWriterInstance = new(MemoryStreamInstance);

        public static byte[] ToByteArray(object obj)
        {
            MemoryStreamInstance.SetLength(0); // Reset the stream length
            var objType = obj.GetType();

            if (!reflectionCache.TryGetValue(objType.FullName!, out PropertyInfo[]? properties))
            {
                properties = objType.GetProperties();
                reflectionCache[objType.FullName!] = properties;
            }

            foreach (var property in properties)
            {
                WritePropertyValue(property.GetValue(obj));
            }

            BinaryWriterInstance.Flush();
            return MemoryStreamInstance.ToArray();
        }

        private static void WritePropertyValue(object? value)
        {
            // TODO: check if this causes issues
            if (value is null)
            {
                BinaryWriterInstance.Write((byte)0);
                return;
            }

            TypeCode typeCode = System.Type.GetTypeCode(value.GetType() ?? typeof(object));
            switch (typeCode)
            {
                case TypeCode.Boolean:
                    BinaryWriterInstance.Write((bool)value);
                    break;
                case TypeCode.Byte:
                    BinaryWriterInstance.Write((byte)value);
                    break;
                case TypeCode.Char:
                    BinaryWriterInstance.Write((char)value);
                    break;
                case TypeCode.Decimal:
                    BinaryWriterInstance.Write((decimal)value);
                    break;
                case TypeCode.Double:
                    BinaryWriterInstance.Write((double)value);
                    break;
                case TypeCode.Int16:
                    BinaryWriterInstance.Write((short)value);
                    break;
                case TypeCode.Int32:
                    BinaryWriterInstance.Write((int)value);
                    break;
                case TypeCode.Int64:
                    BinaryWriterInstance.Write((long)value);
                    break;
                case TypeCode.SByte:
                    BinaryWriterInstance.Write((sbyte)value);
                    break;
                case TypeCode.Single:
                    BinaryWriterInstance.Write((float)value);
                    break;
                case TypeCode.UInt16:
                    BinaryWriterInstance.Write((ushort)value);
                    break;
                case TypeCode.UInt32:
                    BinaryWriterInstance.Write((uint)value);
                    break;
                case TypeCode.UInt64:
                    BinaryWriterInstance.Write((ulong)value);
                    break;
                case TypeCode.String:
                    BinaryWriterInstance.Write((string)value);
                    break;
                case TypeCode.DateTime:
                    BinaryWriterInstance.Write(((DateTime)value).Ticks);
                    break;
                case TypeCode.Object:
                    switch (value)
                    {
                        case Vector2 vector2:
                            BinaryWriterInstance.Write(vector2.X);
                            BinaryWriterInstance.Write(vector2.Y);
                            break;
                        case Vector3 vector3:
                            BinaryWriterInstance.Write(vector3.X);
                            BinaryWriterInstance.Write(vector3.Y);
                            BinaryWriterInstance.Write(vector3.Z);
                            break;
                        case Vector4 vector4:
                            BinaryWriterInstance.Write(vector4.X);
                            BinaryWriterInstance.Write(vector4.Y);
                            BinaryWriterInstance.Write(vector4.Z);
                            BinaryWriterInstance.Write(vector4.W);
                            break;
                        default:
                            throw new NotSupportedException($"Type '{value.GetType().FullName}' is not supported.");
                    }
                    break;
                default:
                    throw new NotSupportedException($"Type '{value.GetType().FullName}' is not supported.");
            }
        }

        public static string GetType(byte[] byteData)
        {
            int offset = 0;
            return ReadString(byteData, ref offset);
        }

        private static int WriteString(byte[] buffer, int offset, string value)
        {
            int byteCount = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, offset + 4);
            WriteInt32(buffer, offset, byteCount);
            return byteCount + 4;
        }

        private static string ReadString(byte[] buffer, ref int offset)
        {
            int byteCount = ReadInt32(buffer, ref offset);
            string value = Encoding.UTF8.GetString(buffer, offset, byteCount);
            offset += byteCount;
            return value;
        }

        private static int WriteInt32(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);
            return 4;
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, 4));
            offset += 4;
            return value;
        }
    }
}
