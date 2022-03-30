using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ULS.Core;

namespace ULS.Core
{
    public enum ReplicatedFieldType : byte
    {
        Reference = 0,
        PrimitiveInt = 1,
        String = 2,
        Vector3 = 3,
        PrimitiveFloat = 4
    }

    /// <summary>
    /// Base class for all network-aware objects.
    /// Replication and RPC calls will only work if the class is derived from NetworkActor
    /// </summary>
    public abstract class NetworkActor
    {
        /// <summary>
        /// Unique network Id. Used to synchronize objects between the client and the server.
        /// Is only unique with regards to the current INetworkOwner
        /// </summary>
        public long UniqueId { get; private set; }

        /// <summary>
        /// Public reference to the INetworkOwner
        /// </summary>
        public INetworkOwner Owner { get; private set; }

        /// <summary>
        /// Whether this actor is only relevena for the specified IWirePacketSender.
        /// Set to null to make relevant for all clients.
        /// If set to null, this actor will be completely unknown to all other clients
        /// </summary>
        public IWirePacketSender? NetworkRelevantOnlyFor { get; set; } = null;

        /// <summary>
        /// Update frequency of variable replication for replication fields 
        /// set to ReplicationStrategy.Automatic.
        /// Represented in ticks.
        /// </summary>
        public long NetUpdateFrequencyTicks = TimeSpan.TicksPerMillisecond * 500;

        /// <summary>
        /// Last time in ticks when replicated fields where last updated
        /// TODO: Needs to be on a per-client basis
        /// </summary>
        public long LastReplicationTimeTicks = DateTimeOffset.MinValue.Ticks;

        /// <summary>
        /// Do not call explicitly.
        /// Only spawn network actors through SpawnNetworkActor<> of the INetworkOwner implementation
        /// </summary>
        public NetworkActor(INetworkOwner setNetworkOwner, long overrideUniqueId)
        {
            Owner = setNetworkOwner;
            if (overrideUniqueId == -1)
            {
                UniqueId = setNetworkOwner.GetNextUniqueId();
            }
            else
            {
                UniqueId = overrideUniqueId;
            }
        }
        
        public string GetReplicationClassName()
        {
            var thisType = GetType();
            var uaa = thisType.GetCustomAttribute<UnrealActorAttribute>();
            if (uaa != null &&
                string.IsNullOrWhiteSpace(uaa.UnrealClassName) == false)
            {
                return uaa.UnrealClassName;
            }

            return thisType.Name;
        }

        public bool ReplicateValues(BinaryWriter writer, bool forced)
        {
            writer.Write((int)0);       // flags
            writer.Write(UniqueId);
            long pos = writer.BaseStream.Position;
            int numberOfSerializedFields = 0;
            writer.Write((int)numberOfSerializedFields);       // Write temporary number of elements (will be overwritten later)

            ReplicateValuesInternal(writer, forced, ref numberOfSerializedFields);

            if (numberOfSerializedFields > 0)
            {
                writer.BaseStream.Seek(pos, SeekOrigin.Begin);
                writer.Write((int)numberOfSerializedFields);       // Write actual number of serialized elements
                writer.BaseStream.Seek(0, SeekOrigin.End);
            }
            return numberOfSerializedFields > 0;
        }

        protected virtual void ReplicateValuesInternal(BinaryWriter writer, bool forced, ref int numberOfSerializedFields)
        {
            // Base implementation does nothing
        }

        public void ApplyReplicatedValues(BinaryReader reader)
        {
            int fieldCount = reader.ReadInt32();
            for (int i = 0; i < fieldCount; i++)
            {
                byte type = reader.ReadByte();
                string fieldName = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                DeserializeFieldInternal(type, fieldName, reader);
            }
        }

        protected virtual void DeserializeFieldInternal(byte type, string fieldName, BinaryReader reader)
        {
            // Base implementation does nothing            
        }

        protected void SerializeRef(BinaryWriter writer, NetworkActor otherActor, string fieldName)
        {
            writer.Write((byte)ReplicatedFieldType.Reference);      // Ref
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));
            if (otherActor == null)
            {
                writer.Write((long)-1);
            }
            else
            {
                writer.Write(otherActor.UniqueId);
            }
        }

        public T? DeserializeRef<T>(BinaryReader reader, INetworkOwner networkOwner) where T : NetworkActor
        {
            long refUniqueId = reader.ReadInt64();
            if (refUniqueId == -1)
            {
                return null;
            }
            return networkOwner.GetNetworkActor<T>(refUniqueId);
        }

        public T? DeserializeRefWithMetadata<T>(BinaryReader reader, INetworkOwner networkOwner) where T : NetworkActor
        {
            byte type = reader.ReadByte();
            string fieldName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
            return DeserializeRef<T>(reader, networkOwner);
        }

        #region PrimitiveInt
        protected void SerializePrimitiveInt<T>(BinaryWriter writer, T value, string fieldName) where T : struct
        {
            writer.Write((byte)ReplicatedFieldType.PrimitiveInt);      // Value
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));            
            int size = Marshal.SizeOf(value);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(value, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            writer.Write(size);
            writer.Write(arr);
        }

        protected T DeserializePrimitiveInt<T>(BinaryReader reader) where T : struct
        {
            T result = default(T);
            int size = Marshal.SizeOf(result);
            byte[] arr = reader.ReadBytes(reader.ReadInt32());

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(arr, 0, ptr, size);

            var obj = Marshal.PtrToStructure(ptr, typeof(T));
            if (obj != null)
            {
                result = (T)obj;
            }

            Marshal.FreeHGlobal(ptr);
            return result;
        }

        protected T DeserializePrimitiveIntWithMetadata<T>(BinaryReader reader) where T : struct
        {
            byte type = reader.ReadByte();
            string fieldName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
            return DeserializePrimitiveInt<T>(reader);
        }
        #endregion

        #region PrimitiveFloat
        protected void SerializePrimitiveFloat<T>(BinaryWriter writer, T value, string fieldName) where T : struct
        {
            writer.Write((byte)ReplicatedFieldType.PrimitiveFloat);      // Value
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));
            int size = Marshal.SizeOf(value);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(value, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            writer.Write(size);
            writer.Write(arr);
        }

        protected T DeserializePrimitiveFloat<T>(BinaryReader reader) where T : struct
        {
            T result = default(T);
            int size = Marshal.SizeOf(result);
            byte[] arr = reader.ReadBytes(reader.ReadInt32());

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(arr, 0, ptr, size);

            var obj = Marshal.PtrToStructure(ptr, typeof(T));
            if (obj != null)
            {
                result = (T)obj;
            }

            Marshal.FreeHGlobal(ptr);
            return result;
        }

        protected T DeserializePrimitiveFloatWithMetadata<T>(BinaryReader reader) where T : struct
        {
            byte type = reader.ReadByte();
            string fieldName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
            return DeserializePrimitiveFloat<T>(reader);
        }
        #endregion

        #region String
        protected void SerializeString(BinaryWriter writer, string value, string fieldName)
        {
            writer.Write((byte)ReplicatedFieldType.String);      // String
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));
            writer.Write(Encoding.UTF8.GetByteCount(value));
            writer.Write(Encoding.UTF8.GetBytes(value));
        }

        protected string DeserializeString(BinaryReader reader)
        {
            return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        }

        protected string DeserializeStringWithMetadata(BinaryReader reader)
        {
            byte type = reader.ReadByte();
            string fieldName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
            return DeserializeString(reader);
        }
        #endregion

        #region Vector3
        protected void SerializeVector3(BinaryWriter writer, System.Numerics.Vector3 value, string fieldName)
        {
            writer.Write((byte)ReplicatedFieldType.Vector3);
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        protected System.Numerics.Vector3 DeserializeVector3(BinaryReader reader)
        {
            return new System.Numerics.Vector3(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
        }

        protected System.Numerics.Vector3 DeserializeVector3WithMetadata(BinaryReader reader)
        {
            byte type = reader.ReadByte();
            string fieldName = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
            return DeserializeVector3(reader);
        }
        #endregion

        public bool ShouldSendTo(IWirePacketSender? relevantTarget)
        {
            if (NetworkRelevantOnlyFor == null)
            {
                return true;
            }

            return NetworkRelevantOnlyFor == relevantTarget;
        }

        public void ProcessRpcMethod(BinaryReader reader)
        {
            ProcessRpcMethodInternal(reader);
        }

        protected virtual void ProcessRpcMethodInternal(BinaryReader reader)
        {
            // 
        }

        public void Client_ProcessRpcMethod(BinaryReader reader)
        {
            Client_ProcessRpcMethodInternal(reader);
        }

        protected virtual void Client_ProcessRpcMethodInternal(BinaryReader reader)
        {
            // 
        }
    }
}
