using System;

namespace ULS.Core
{
    /// <summary>
    /// Marks a function as an RPC call.
    /// The C# source generator will generate the appropriate code to send changes
    /// over the network.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Event)]
    public class RpcCallAttribute : Attribute
    {
        public bool IsBroadcast { get; set; } = true;

        public string[] ParameterNames { get; set; } = Array.Empty<string>();
    }
}