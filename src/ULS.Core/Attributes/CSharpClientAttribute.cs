using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core
{
    /// <summary>
    /// Indicates a client written in C# instead of Unreal. 
    /// This is useful for bots running on a server.
    /// </summary>
    public class CSharpClientAttribute : Attribute
    {
    }
}
