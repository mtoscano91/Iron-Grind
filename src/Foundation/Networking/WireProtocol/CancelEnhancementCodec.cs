using System;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="CancelEnhancement"/> (no body).</summary>
    public static class CancelEnhancementCodec
    {
        /// <summary>Writes nothing; returns 0 (<see cref="CancelEnhancement.BodySize"/>).</summary>
        public static int WriteBody(Span<byte> destination) => CancelEnhancement.BodySize;

        /// <summary>Accepts any body (there is nothing to decode); always <see langword="true"/>.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body) => true;
    }
}
