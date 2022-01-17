using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ULS.Core.IntegratedTypes
{
    public struct Transform
    {
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;
    }
}
