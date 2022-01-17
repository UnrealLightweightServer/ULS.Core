using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ULS.Core;

namespace ULS.Core
{
    public enum RpcParameterType
    {
        String,
        Int,
        Long,
        Float,
        Object
    }

    /// <summary>
    /// RPC packet payload
    /// 
    /// Uses JSON serialization. Will be refactored to use binary transfer later.
    /// </summary>
    public class RpcPayload
    {
        public class RpcParameter
        {
            public string Name { get; set; } = "_not_set_";

            public RpcParameterType Type { get; set; } = RpcParameterType.Object;

            public object? Value { get; set; } = null;
        }

        public string MethodName { get; set; } = "_unknown_";

        public string ReturnType { get; set; } = "void";

        public string ReturnValue { get; set; } = string.Empty;

        public long UniqueMsgId { get; set; } = -1;

        public List<RpcParameter> Parameters { get; set; } = new List<RpcParameter>();

        public WirePacket GetWirePacket()
        {
            byte[] data = null;// JsonSerializer.SerializeToUtf8Bytes(this);
            return new WirePacket(WirePacketType.Rpc, data);
        }

        public static RpcPayload? FromJsonBytes(byte[] data)
        {
            return null;/* JsonSerializer.Deserialize<RpcPayload>(data, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });*/
        }

        public T? Get<T>(string id, out bool found) where T : struct
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    found = true;
                    /*return JsonSerializer.Deserialize<T>((string)parameter.Value, new JsonSerializerOptions()
                    {
                        IncludeFields = true,
                    });*/
                }
            }
            return default(T?);
        }

        public int GetInt32(string id, out bool found)
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    switch (parameter.Type)
                    {
                        case RpcParameterType.Int:
                            found = true;
                            return 0;// ((JsonElement)parameter.Value).GetInt32();

                        default:
                            return 0;
                    }
                }
            }
            return 0;
        }

        public long GetInt64(string id, out bool found)
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    switch (parameter.Type)
                    {
                        case RpcParameterType.Long:
                            found = true;
                            return 0;// ((JsonElement)parameter.Value).GetInt64();

                        default:
                            return 0;
                    }
                }
            }
            return 0;
        }

        public long GetObject(string id, out bool found)
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    switch (parameter.Type)
                    {
                        case RpcParameterType.Object:
                            found = true;
                            return 0;// ((JsonElement)parameter.Value).GetInt64();

                        default:
                            return 0;
                    }
                }
            }
            return 0;
        }

        public float GetFloat(string id, out bool found)
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    switch (parameter.Type)
                    {
                        case RpcParameterType.Float:
                            found = true;
                            return 0;// ((JsonElement)parameter.Value).GetSingle();

                        default:
                            return 0;
                    }
                }
            }
            return 0;
        }

        public string? GetString(string id, out bool found)
        {
            found = false;

            for (int i = 0; i < Parameters.Count; i++)
            {
                RpcParameter parameter = Parameters[i];
                if (parameter.Name == id &&
                    parameter.Value != null)
                {
                    switch (parameter.Type)
                    {
                        case RpcParameterType.String:
                            found = true;
                            return null;// ((JsonElement)parameter.Value).GetString();

                        default:
                            return string.Empty;
                    }
                }
            }
            return string.Empty;
        }

        public void Set<T>(string id, T value)
        {
            Parameters.Add(new RpcParameter()
            {
                Name = id,
                /*Value = JsonSerializer.Serialize<T>(value, new JsonSerializerOptions()
                {
                    IncludeFields = true,
                }),*/
                Type = RpcParameterType.Object
            });
        }

        public T? GetReturnValue<T>()
        {
            if (ReturnValue is string strVal)
            {
                /*return JsonSerializer.Deserialize<T>(strVal, new JsonSerializerOptions()
                {
                    IncludeFields = true,
                });*/
            }
            return default(T);
        }

        public void SetReturnValue<T>(T value)
        {
            /*ReturnValue = JsonSerializer.Serialize<T>(value, new JsonSerializerOptions()
            {
                IncludeFields = true,
            });*/
        }
    }
}
