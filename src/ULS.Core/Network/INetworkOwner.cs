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
        /// Invoked when <see cref="SpawnNetworkActor{T}(long)"/> is called to return
        /// the next uniqueId.
        /// The implementation must ensure that each uniqueId is only given out once per session.
        /// </summary>
        long GetNextUniqueId();

        /// <summary>
        /// Returns the network actor identified through the uniqueId
        /// </summary>
        T GetNetworkActor<T>(long uniqueId) where T : NetworkActor;

        /// <summary>
        /// Creates and registers and new network actor. Always create network-aware actors using
        /// this function.
        /// Leave <paramref name="overrideUniqueId"/> at -1 unless you know what you're doing.
        /// </summary>
        T SpawnNetworkActor<T>(long overrideUniqueId = -1) where T : NetworkActor;

        /// <summary>
        /// Despawns the specified network actor
        /// </summary>
        void DespawnNetworkActor<T>(T actor) where T : NetworkActor;

        /// <summary>
        /// Used to manually invoke a member replication. Should not be called by external
        /// code unless the ReplicationStrategy is set to manual.
        /// </summary>
        void ReplicateValues();
    }
}
