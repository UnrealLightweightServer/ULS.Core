using System;

namespace ULS.Core
{
    public enum CallStrategy
    {
        GenerateInWrapperClass,
        Reflection
    }

    /// <summary>
    /// Marks a function as an RPC call.
    /// The C# source generator will generate the appropriate code to send changes
    /// over the network.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Event)]
    public class RpcCallAttribute : Attribute
    {
        public CallStrategy CallStrategy { get; set; } = CallStrategy.GenerateInWrapperClass;

        public string[] ParameterNames { get; set; } = Array.Empty<string>();
    }
}