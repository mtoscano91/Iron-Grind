using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Seam for the two joiner-only messages of CR-ZI-8 step 7 (Zone Instancing Story 010, user decision 2026-10-22).
    /// Their codecs are not written yet (schema pending OQ-NC-SER-2), so <see cref="ZoneEntryCompletion"/> calls this
    /// interface and the composition supplies <see cref="LoggingZoneEntryResponder"/> until the codecs exist.
    /// Tick thread only. An implementation should not throw.
    /// </summary>
    public interface IZoneEntryResponder
    {
        /// <summary>Sends <c>SessionReady</c> to the joining client.</summary>
        /// <param name="clientId">The joining client's connection.</param>
        void SendSessionReady(uint clientId);

        /// <summary>Sends <c>SkillCooldownSnapshot</c> to the joining client.</summary>
        /// <param name="clientId">The joining client's connection.</param>
        /// <param name="entityId">The joining player's EntityID.</param>
        void SendSkillCooldownSnapshot(uint clientId, EntityID entityId);
    }
}
