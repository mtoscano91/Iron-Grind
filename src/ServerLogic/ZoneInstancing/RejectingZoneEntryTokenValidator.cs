using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Stand-in <see cref="IZoneEntryTokenValidator"/> that rejects every token and logs once per instance that
    /// validation is not implemented (Zone Instancing Story 009, user decision 2026-10-22).
    /// </summary>
    public sealed class RejectingZoneEntryTokenValidator : IZoneEntryTokenValidator
    {
        /// <summary>The line logged on the first rejection.</summary>
        public const string NOT_IMPLEMENTED_MESSAGE = "[ZoneServer] zone entry token validation not implemented";

        private bool _logged;

        /// <inheritdoc/>
        public bool IsValid(in ZoneEntryRequest request)
        {
            if (!_logged)
            {
                _logged = true;
                Debug.LogWarning(NOT_IMPLEMENTED_MESSAGE);
            }

            return false;
        }
    }
}
