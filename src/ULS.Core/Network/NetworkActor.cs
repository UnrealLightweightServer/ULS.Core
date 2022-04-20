using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ULS.Core;

namespace ULS.Core
{
    /// <summary>
    /// Base class for all network-aware actors.
    /// Unreal AActors must map to NetworkActor-derived classes.
    /// </summary>
    public abstract class NetworkActor : NetworkObject
    {
        public NetworkActor(INetworkOwner setNetworkOwner, long overrideUniqueId)
            : base(setNetworkOwner, overrideUniqueId)
        {
        }
    }
}
