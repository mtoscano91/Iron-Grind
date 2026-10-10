using System;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="CloseNPCInteraction"/> (no body).</summary>
    public static class CloseNPCInteractionCodec
    {
        /// <summary>Writes nothing; returns 0 (<see cref="CloseNPCInteraction.BodySize"/>).</summary>
        public static int WriteBody(Span<byte> destination) => CloseNPCInteraction.BodySize;

        /// <summary>Accepts any body (there is nothing to decode); always <see langword="true"/>.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body) => true;
    }
}
