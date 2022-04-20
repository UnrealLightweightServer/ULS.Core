using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ULS.Core
{
    /// <summary>
    /// INetworkOwner is the core management class in ULS. Must be implemeted.
    /// Manages client connections.
    /// </summary>
    public interface INetworkOwner
    {
        /// <summary>
        /// Invoked when <see cref="SpawnNetworkObject{T}(long)"/> is called to return
        /// the next uniqueId.
        /// The implementation must ensure that each uniqueId is only given out once per session.
        /// </summary>
        long GetNextUniqueId();

        /// <summary>
        /// Returns the network object identified through the uniqueId
        /// </summary>
        T? GetNetworkObject<T>(long uniqueId) where T : NetworkObject;

        /// <summary>
        /// Creates and registers a new network object. Always create network-aware objects using
        /// this function.
        /// Leave <paramref name="overrideUniqueId"/> at -1 unless you know what you're doing.
        /// </summary>
        T SpawnNetworkObject<T>(IWirePacketSender? networkRelevantOnlyFor = null, long overrideUniqueId = -1) where T : NetworkObject;

        /// <summary>
        /// Despawns the specified network object
        /// </summary>
        void DespawnNetworkObject<T>(T actor) where T : NetworkObject;

        /// <summary>
        /// Creates and registers a new network actor. Always create network-aware actors using
        /// this function.
        /// Leave <paramref name="overrideUniqueId"/> at -1 unless you know what you're doing.
        /// </summary>
        T SpawnNetworkActor<T>(IWirePacketSender? networkRelevantOnlyFor = null, long overrideUniqueId = -1) where T : NetworkActor;

        /// <summary>
        /// Despawns the specified network object
        /// </summary>
        void DespawnNetworkActor<T>(T actor) where T : NetworkObject;

        /// <summary>
        /// Directly replicate the content to the client(s).
        /// Wrap the byte array into a replication packet.
        /// </summary>
        void ReplicateValueDirect(NetworkObject valueOwner, byte[] replicationData);

        /// <summary>
        /// Used to manually invoke a member replication. Should only be called by external
        /// code if the ReplicationStrategy is set to manual or automatic.
        /// </summary>
        void ReplicateValues();

        /// <summary>
        /// Send RPC call to the specified target or all known targets if target is null
        /// </summary>
        void SendRpc(IWirePacketSender? target, byte[] data);
    }
}
