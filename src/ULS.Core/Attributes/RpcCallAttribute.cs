using System;

namespace ULS.Core
{
    public enum CallStrategy
    {
        /// <summary>
        /// Generates a BlueprintImplementableEvent in the header file of the 
        /// class defined in the <see cref="UnrealClassAttribute"/>.
        /// Generates calling code in the implementation file of the 
        /// class defined in the <see cref="UnrealClassAttribute"/>.
        /// Does not use any reflection beyond what Unreal does itself
        /// to invoke BP events.
        /// </summary>
        GenerateInWrapperClass,

        /// <summary>
        /// Generates code in the implementation file of the class defined 
        /// in the <see cref="UnrealClassAttribute"/>.
        /// Uses reflection to lookup the function name and header of the Unreal 
        /// function and then generates code to properly invoke it.
        /// </summary>
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