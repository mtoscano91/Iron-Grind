using System;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="NPCInteractionOpened"/> (no body).</summary>
    public static class NPCInteractionOpenedCodec
    {
        /// <summary>Writes nothing; returns 0 (<see cref="NPCInteractionOpened.BodySize"/>).</summary>
        public static int WriteBody(Span<byte> destination) => NPCInteractionOpened.BodySize;

        /// <summary>Accepts any body (there is nothing to decode); always <see langword="true"/>.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body) => true;
    }
}
