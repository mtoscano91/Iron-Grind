namespace IronGrind.ServerLogic.Gateway.Link
{
    /// <summary>Outcome of <see cref="GatewayLinkCodec.TryReadFrame"/>.</summary>
    public enum GatewayLinkFrameStatus
    {
        /// <summary>The buffer holds only part of a frame; read more bytes and call again.</summary>
        NeedMoreBytes = 0,

        /// <summary>One complete frame was read; trailing bytes are left for the next call.</summary>
        Frame = 1,

        /// <summary>The frame is invalid (length 0, above the cap, or unknown type); the caller closes the link.</summary>
        ProtocolViolation = 2,
    }
}
