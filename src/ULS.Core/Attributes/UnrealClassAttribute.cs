using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core
{
    public class UnrealClassAttribute : Attribute
    {
        public string ClassName { get; set; } = string.Empty;
    }
}
