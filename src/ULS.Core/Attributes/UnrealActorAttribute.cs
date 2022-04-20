using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core
{
    /// <summary>
    /// Declares the class name used by the client.
    /// Must be set to make SpawnNetworkObject work properly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UnrealActorAttribute : Attribute
    {
        public string UnrealClassName { get; set; } = string.Empty;
    }
}
