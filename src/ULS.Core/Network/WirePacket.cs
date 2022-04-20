using System;
using System.Buffers.Binary;
using System.Text;

namespace ULS.Core
{
    public enum WirePacketType : int
    {
        ConnectionRequest = 0,          // Sent by client. Request to establish connection. Followed by ConnectionResponse
        ConnectionResponse = 1,         // Sent by server upon receiving a ConnectionRequest. Contains "success true/false"
        ConnectionEnd = 2,              // Sent by server when the connection is closed gracefully from the server side (i.e. when the "world" is shut down)

        Replication = 110,              // Replication message. Sent by the server only.
        SpawnActor = 111,               // Spawns a new network actor on the client. Sent by the server only.
        DespawnActor = 112,             // Despawns a network actor on the client. Sent by the server only.
        NewObject = 113,                // Creates a new UObject based object on the client. Sent by the server only.
        DestroyObject = 114,            // Destroy a UObject based object on the client. Sent by the server only.
        RpcCall = 115,                  // Serialized RpcCall. Can be sent by both parties.
        RpcCallResponse = 116,          // Serialized response to an RpcCall. Can be sent by both parties.

        Custom = 200,                   // Custom, user-specific data. Ignored in low-level operations
    }

    public interface IWirePacketSender
    {
        void SendPacket(WirePacket packet);
    }

    /// <summary>
    /// The WirePacket is the base packet structure for all network activies in ULS.
    /// The Client and Server will only understand and process packet data in this format.
    /// </summary>
    public class WirePacket
    {
        private const int HeaderSize = 4;
        public WirePacketType PacketType { get; private set; } = 0;
        public ReadOnlyMemory<byte> Payload => RawData.Slice(HeaderSize);

        public Memory<byte> RawData = Array.Empty<byte>();

        public WirePacket(Memory<byte> rawData)
        {
            RawData = rawData;
            PacketType = (WirePacketType)BinaryPrimitives.ReadInt32LittleEndian(RawData.Slice(0, HeaderSize).Span);
        }

        public WirePacket(WirePacketType packetType, byte[] payload)
        {
            PacketType = packetType;
            RawData = new byte[payload.Length + HeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(RawData.Slice(0, HeaderSize).Span, (int)packetType);
            payload.CopyTo(RawData.Slice(HeaderSize));
        }

        public void WriteInt16(short val, int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(short))
            {
                ExtendRawData(index + sizeof(short));
            }
            BinaryPrimitives.WriteInt16LittleEndian(RawData.Slice(index).Span, val);
        }

        public short ReadInt16(int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(short))
            {
                return 0;
            }
            return BinaryPrimitives.ReadInt16LittleEndian(RawData.Slice(index).Span);
        }

        public void WriteInt32(int val, int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(int))
            {
                ExtendRawData(index + sizeof(int));
            }
            BinaryPrimitives.WriteInt32LittleEndian(RawData.Slice(index).Span, val);
        }

        public int ReadInt32(int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(int))
            {
                return 0;
            }
            return BinaryPrimitives.ReadInt32LittleEndian(RawData.Slice(index).Span);
        }

        public void WriteInt64(long val, int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(long))
            {
                ExtendRawData(index + sizeof(long));
            }
            BinaryPrimitives.WriteInt64LittleEndian(RawData.Slice(index).Span, val);
        }

        public long ReadInt64(int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(long))
            {
                return 0;
            }
            return BinaryPrimitives.ReadInt64LittleEndian(RawData.Slice(index).Span);
        }

        public void WriteBytes(int index, byte[] data)
        {
            index += HeaderSize;
            int count = data.Length;
            if (RawData.Length < index + count)
            {
                ExtendRawData(index + count);
            }
            data.CopyTo(RawData.Slice(index));
        }

        public byte[] ReadBytes(int index, int count)
        {
            index += HeaderSize;
            if (RawData.Length < index + count)
            {
                return Array.Empty<byte>();
            }
            return RawData.Slice(index, count).ToArray();
        }

        public string ReadUnrealString(int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(int))
            {
                return string.Empty;
            }
            int strlen = BinaryPrimitives.ReadInt32LittleEndian(RawData.Slice(index).Span);
            if (strlen <= 0)
            {
                return string.Empty;
            }
            if (RawData.Length < index + sizeof(int) + strlen)
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(RawData.Slice(index + sizeof(int), strlen).ToArray());
        }

        public byte[] ReadUnrealByteArray(int index)
        {
            index += HeaderSize;
            if (RawData.Length < index + sizeof(int))
            {
                return Array.Empty<byte>();
            }
            int strlen = BinaryPrimitives.ReadInt32LittleEndian(RawData.Slice(index).Span);
            if (strlen <= 0)
            {
                return Array.Empty<byte>();
            }
            if (RawData.Length < index + sizeof(int) + strlen)
            {
                return Array.Empty<byte>();
            }
            return RawData.Slice(index + sizeof(int), strlen).ToArray();
        }

        private void ExtendRawData(int newSize)
        {
            if (newSize <= RawData.Length)
            {
                return;
            }
            Memory<byte> newRawData = new byte[newSize];
            RawData.CopyTo(newRawData);
            RawData = newRawData;
        }
    }
}
