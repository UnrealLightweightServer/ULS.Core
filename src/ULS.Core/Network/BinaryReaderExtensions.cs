using System;
using System.Collections.Generic;
using System.Text;

namespace ULS.Core.Network
{
    public static class BinaryReaderExtensions
    {
        public static string ReadUnrealString(this BinaryReader reader)
        {
            Int32 len = reader.ReadInt32();
            return Encoding.UTF8.GetString(reader.ReadBytes(len));
        }

        public static byte[] ReadUnrealByteArray(this BinaryReader reader)
        {
            Int32 len = reader.ReadInt32();
            return reader.ReadBytes(len);
        }
    }
}
