using System;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="RejectedNotInTownHub"/> (no body).</summary>
    public static class RejectedNotInTownHubCodec
    {
        /// <summary>Writes nothing; returns 0 (<see cref="RejectedNotInTownHub.BodySize"/>).</summary>
        public static int WriteBody(Span<byte> destination) => RejectedNotInTownHub.BodySize;

        /// <summary>Accepts any body (there is nothing to decode); always <see langword="true"/>.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body) => true;
    }
}
