using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core
{
    public enum ReplicationStrategy
    {
        Automatic,
        Manual,
        Immediate
    }

    /// <summary>
    /// Marks a class field as replicated.
    /// The C# source generator will generate the appropriate code to send changes
    /// over the network.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ReplicateAttribute : Attribute
    {
        /// <summary>
        /// Set to the client member name if it is not the same as on the server.
        /// </summary>
        public string? ClientMemberName { get; set; } = null;

        public ReplicationStrategy ReplicationStrategy { get; set; } = ReplicationStrategy.Automatic;
    }
}
