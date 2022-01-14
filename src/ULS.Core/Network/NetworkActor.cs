using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ULS.Core;

namespace ULS.Core
{
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
        public long UniqueId { get; init; }

        /// <summary>
        /// Public reference to the INetworkOwner
        /// </summary>
        public INetworkOwner Owner { get; init; }

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

        public virtual void ApplyReplicatedValues(BinaryReader reader)
        {
            // Base implementation does nothing
        }

        protected void SerializeRef(BinaryWriter writer, NetworkActor otherActor, string fieldName)
        {
            writer.Write((byte)0);      // Ref
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

        protected void SerializeValue<T>(BinaryWriter writer, T value, string fieldName) where T : struct
        {
            writer.Write((byte)1);      // Value
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

        protected T DeserializeValue<T>(BinaryReader reader) where T : struct
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

        protected void SerializeString(BinaryWriter writer, string value, string fieldName)
        {
            writer.Write((byte)2);      // String
            writer.Write(Encoding.ASCII.GetByteCount(fieldName));
            writer.Write(Encoding.ASCII.GetBytes(fieldName));
            writer.Write(Encoding.UTF8.GetByteCount(value));
            writer.Write(Encoding.UTF8.GetBytes(value));
        }

        protected string DeserializeString(BinaryReader reader)
        {
            return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        }
    }
}
