using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core
{
    /// <summary>
    /// Declares project-wide properties.
    /// At least one class must have this attribute set to make code generation work 
    /// for Unreal projects.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UnrealProjectAttribute : Attribute
    {
        public string ProjectName { get; set; } = string.Empty;

        public string ProjectFile { get; set; } = string.Empty;

        public string Module { get; set; } = string.Empty;

        public bool IsCodeGenerationEnabled { get; set; } = true;
    }
}
