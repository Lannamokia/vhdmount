namespace VHDMounter.RustDeskBridge.Session
{
    /// <summary>
    /// Bridge_Session 生命周期内的三阶段状态（design §"BridgeWorker 状态机"）。
    /// 状态机 / 帧路由器在 Wave 5 任务 9.4 落地，本枚举仅是数据契约。
    /// </summary>
    internal enum BridgeSessionState
    {
        /// <summary>已 ConnectNamedPipe 但还未收到合法握手帧。</summary>
        Connecting = 0,

        /// <summary>握手通过、Report / Log / PeerApproval 路由生效。</summary>
        Handshaked = 1,

        /// <summary>正在关闭中（Revocation / 协议错误 / 客户端断开）。</summary>
        Closing = 2,
    }
}
