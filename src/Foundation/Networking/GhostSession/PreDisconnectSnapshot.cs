namespace IronGrind.Networking
{
    /// <summary>
    /// CGS-3's pre-disconnect snapshot for a single character, captured at the moment ghost
    /// promotion begins — the values <see cref="GhostCleanupSequencer"/> persists verbatim on
    /// ghost-period TTL expiry (AC-CGS-1) or ghost death (AC-CGS-2), overriding whatever ghost-period
    /// combat changes occurred while the character was disconnected and ghosted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately minimal field list — HP and position only, not CGS-3's full field list
    /// (judgment call, approved before implementation):</b> the GDD's CGS-3 Implementation Notes
    /// enumerate a much larger snapshot: <c>currentHP</c>, <c>maxHP</c>, <c>currentMP</c>,
    /// <c>maxMP</c>, <c>Position</c>, <c>Rotation</c>, <c>Inventory</c>, <c>XP</c>, <c>Level</c>,
    /// <c>heldFreePoints</c>, <c>allocatedStats</c>, <c>equipmentAppearanceFlags</c>,
    /// <c>disconnectTickNumber</c>, <c>activeBuffs</c>, <c>skillCooldowns</c>. None of the owning
    /// systems for most of those fields exist yet in this codebase — no Character Stats, Inventory,
    /// or Buffs system — the same forward-dependency scope discipline every prior story in this
    /// codebase applies. This story's own 4 blocking ACs (AC-CGS-1 through AC-CGS-4) only ever
    /// assert against HP and position, so this struct models exactly those two fields and no more.
    /// This is an intentional scope decision, not an oversight — a future story extending ghost
    /// cleanup to cover MP/inventory/buffs/etc. is expected to widen this struct (or introduce a
    /// superseding one) once those owning systems exist. Do not mistake the omission for a bug.
    /// </para>
    /// <para>
    /// <b><see cref="RespawnPosition"/> is the zone-entry respawn position, not the disconnect-moment
    /// position:</b> it is captured into the snapshot alongside <see cref="Hp"/> at
    /// <see cref="PreDisconnectSnapshotWal.TryWriteSnapshot"/> time so that, if the character later
    /// dies while ghosted (CGS-5), the caller can source both the snapshot HP and the substituted
    /// respawn position from this same WAL entry rather than performing a second, separate zone
    /// lookup at cleanup time. It is not read or asserted by AC-CGS-1/AC-CGS-4 (the TTL-expiry path
    /// has no position requirement) — only by AC-CGS-2's ghost-death path.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var snapshot = new PreDisconnectSnapshot(hp: 42, respawnPosition: (0, 0, 0));
    /// wal.TryWriteSnapshot(characterId: 555u, disconnectTickNumber: 1000u, snapshot);
    /// </code>
    /// </example>
    public readonly struct PreDisconnectSnapshot
    {
        /// <summary>The character's HP at the moment of disconnect — never the ghost-period-reduced HP.</summary>
        public readonly int Hp;

        /// <summary>The zone-entry respawn position, substituted on ghost death (CGS-5). See remarks.</summary>
        public readonly (short x, short y, short z) RespawnPosition;

        /// <summary>Constructs a snapshot with the given HP and respawn position.</summary>
        /// <param name="hp">The character's HP at the moment of disconnect.</param>
        /// <param name="respawnPosition">The zone-entry respawn position.</param>
        public PreDisconnectSnapshot(int hp, (short x, short y, short z) respawnPosition)
        {
            Hp = hp;
            RespawnPosition = respawnPosition;
        }
    }
}
