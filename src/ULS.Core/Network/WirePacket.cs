using System;
using System.Buffers.Binary;

namespace ULS.Core
{
    public enum WirePacketType : int
    {
        WorldJoinRequest = 0,           // Sent by player. Request to join a world. Returns WorldInvalid or WorldJoinSuccess
        WorldLeaveRequest = 1,          // Sent by player. Request to leave world. Returns WorldLeaveSuccess
        WorldJoinComplete = 2,          // Sent by player. Player has entered world and is ready for start.

        WorldInvalid = 10,              // Sent by server, if WorldJoinRequest is invalid
        WorldJoinSuccess = 11,          // Sent by server, if WorldJoinRequest is valid
        WorldLeaveSuccess = 12,         // Sent by server, if WorldLeaveRequest is valid
        WorldBegin = 13,                // Sent by server. World enters running state
        WorldEnd = 14,                  // Sent by server. World has shut down

        Rpc = 100,                      // Generic remote procedure call. Can be sent by both parties.
        RpcResponse = 101,              // Response for an RPC. Can be sent by both parties.

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
        public WirePacketType PacketType { get; private set; } = 0;
        public ReadOnlyMemory<byte> Payload => RawData.Slice(4);

        public Memory<byte> RawData = Array.Empty<byte>();

        public WirePacket(Memory<byte> rawData)
        {
            RawData = rawData;
            PacketType = (WirePacketType)BinaryPrimitives.ReadInt32BigEndian(RawData.Slice(0, 4).Span);
        }

        public WirePacket(WirePacketType packetType, byte[] payload)
        {
            PacketType = packetType;
            RawData = new byte[payload.Length + 4];
            BinaryPrimitives.WriteInt32BigEndian(RawData.Slice(0, 4).Span, (int)packetType);
            payload.CopyTo(RawData.Slice(4));
        }

        public void WriteInt32(int val, int index)
        {
            index += 4;
            if (RawData.Length < index + sizeof(int))
            {
                ExtendRawData(index + sizeof(int));
            }
            BinaryPrimitives.WriteInt32LittleEndian(RawData.Slice(index).Span, val);
        }

        public void WriteInt64(long val, int index)
        {
            index += 4;
            if (RawData.Length < index + sizeof(long))
            {
                ExtendRawData(index + sizeof(long));
            }
            BinaryPrimitives.WriteInt64LittleEndian(RawData.Slice(index).Span, val);
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
