using NUnit.Framework;
using SimpleNetworking.Client;
using SimpleNetworking.Packets;
using SimpleNetworking.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json.Serialization;

namespace SimpleTcpTest
{
    internal class TestObjectToSerialize
    {
        public int IntValue { get; set; }
        public string StringValue { get; set; }
        public ushort LongValue { get; set; }
        public Vector3 Vector { get; set; }

        public TestObjectToSerialize(int intValue, string stringValue, ushort longValue, Vector3 vector3)
        {
            IntValue = intValue;
            StringValue = stringValue;
            LongValue = longValue;
            Vector = vector3;
        }

        public TestObjectToSerialize(byte[] data)
        {
            using BinaryReader reader = new(new MemoryStream(data));
            IntValue = reader.ReadInt32();
            StringValue = reader.ReadString();
            LongValue = reader.ReadUInt16();
            Vector = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public override string ToString()
        {
            return $"IntValue: {IntValue}, StringValue: {StringValue}, LongValue: {LongValue}, Vector: {Vector}";
        }
    }

    internal class TestObjectToSerializePacket(int intValue, string stringValue, TwoDPoint twoDPoint) : Packet<TestObjectToSerializePacket>
    {
        public int IntValue { get; set; } = intValue;
        public string StringValue { get; set; } = stringValue;
        public TwoDPoint TwoDPoint { get; set; } = twoDPoint;
    }

    internal sealed class TwoDPoint
    {
        public int X { get; set; }
        public int Y { get; set; }

        public TwoDPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void TestJsonPacket()
        {
            TestObjectToSerializePacket packet = new (42, "Hello, World!", new TwoDPoint(0,3));
            
            string serialized = packet.Serialize();

            Debug.WriteLine(serialized);

            TestObjectToSerializePacket deserialized = TestObjectToSerializePacket.Deserialize(serialized);

            Debug.WriteLine(deserialized);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.IntValue, Is.EqualTo(packet.IntValue));
                Assert.That(deserialized.StringValue, Is.EqualTo(packet.StringValue));
                Assert.That(deserialized.Type, Is.EqualTo(nameof(TestObjectToSerializePacket)));
                Assert.That(deserialized.Type, Is.EqualTo(Packet.GetType(serialized)));
                Assert.That(deserialized.Guid, Is.Not.EqualTo(Guid.Empty));
            });
        }

        [Test]
        public void TestBitPacket()
        {
            TestObjectToSerialize testObject = new(42, "Hello, World!", ushort.MaxValue, new Vector3(1,2,3));

            BitPacket packet = new(nameof(TestObjectToSerialize), BitPacket.ToByteArray(testObject));

            byte[] serialized = packet.Serialize();
            BitPacket deserialized = BitPacket.Deserialize(serialized);
            TestObjectToSerialize deserialized2 = new(deserialized.Data);

            Debug.WriteLine(deserialized2.ToString());

            Assert.Multiple(() =>
            {
                Assert.That(deserialized2.IntValue, Is.EqualTo(testObject.IntValue));
                Assert.That(deserialized2.StringValue, Is.EqualTo(testObject.StringValue));
                Assert.That(deserialized.Type, Is.EqualTo(nameof(TestObjectToSerialize)));
                Assert.That(deserialized.Type, Is.EqualTo(BitPacket.GetType(serialized)));
                Assert.That(deserialized.Length, Is.EqualTo(packet.Length));
                Assert.That(deserialized.Guid, Is.Not.EqualTo(Guid.Empty));
            });
        }

        [Test]
        public void TestTcpConnection()
        {
            using SimpleServer server = new SimpleServer(SimpleNetworking.Protocol.Tcp, 1, 12345, IPAddress.Loopback);
            using SimpleClient client = new SimpleClient(SimpleNetworking.Protocol.Tcp, 12345, IPAddress.Loopback);

            server.OnMessageReceived += (e) =>
            {
                Assert.That(e.MessageString, Is.EqualTo("Hello, World!"));
                Assert.That(e.Sender, Is.EqualTo(client.LocalEndPoint));
            };

            server.StartListen(new CancellationTokenSource().Token);

            client.OnMessageReceived += (e) =>
            {
                Assert.That(e.MessageString, Is.EqualTo("Hello, World!"));
                Assert.That(e.Sender, Is.EqualTo(server.LocalEndPoint));
            };

            client.OnConnectionOpened += (e) =>
            {
                client.Send("Hello, World!");
                server.SendToAll("Hello, World!");
            };

            client.Connect(new CancellationTokenSource().Token);
        }
    }
}