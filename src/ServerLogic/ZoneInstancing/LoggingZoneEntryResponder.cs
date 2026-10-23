using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Stand-in <see cref="IZoneEntryResponder"/> that sends nothing and logs once per instance that
    /// <c>SessionReady</c> is not implemented (Zone Instancing Story 010, user decision 2026-10-22).
    /// </summary>
    public sealed class LoggingZoneEntryResponder : IZoneEntryResponder
    {
        /// <summary>The line logged on the first call.</summary>
        public const string NOT_IMPLEMENTED_MESSAGE = "[ZoneServer] SessionReady not implemented";

        private bool _logged;

        /// <inheritdoc/>
        public void SendSessionReady(uint clientId)
        {
            LogOnce();
        }

        /// <inheritdoc/>
        public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
        {
            LogOnce();
        }

        private void LogOnce()
        {
            if (!_logged)
            {
                _logged = true;
                Debug.LogWarning(NOT_IMPLEMENTED_MESSAGE);
            }
        }
    }
}
