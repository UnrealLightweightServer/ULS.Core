using System;
using ULS.Core;

namespace ULS.Core
{
    public interface IRpcSender
    {
        void SendRpcToTarget(IRpcTarget rpcTarget, WirePacket rpcPacket);
        void BroadcastRpc(WirePacket rpcPacket);
        void AddRpcInterceptHandlerToTarget(IRpcTarget target, long uniqueMessageId, Action<RpcPayload> handler);
    }

    public interface IRpcTarget
    {
        void SendRpc(WirePacket packet);

        void AddInterceptHandler(Action<RpcPayload> handler, long uniqueMessageHandlerId);
    }
}
