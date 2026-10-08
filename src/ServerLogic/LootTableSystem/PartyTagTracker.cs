using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Tracks per-party damage on each mob and decides who owns its loot tag
    /// (design/gdd/loot-table-system.md CR-LT-3 damage accumulation, CR-LT-4 threshold lock and
    /// fallback, F-LT-3 tag threshold).
    /// </summary>
    public sealed class PartyTagTracker
    {
        private struct PartyEntry
        {
            public PartyID Party;
            public uint CumulativeDamage;
            public uint FirstDamageTick;
        }

        private sealed class MobRecord
        {
            public int Threshold;
            public bool IsLocked;
            public PartyID LockedOwner;
            // First-recorded order: a full tie in the fallback keeps the earlier-recorded party.
            public readonly List<PartyEntry> Entries = new List<PartyEntry>();
        }

        private readonly IPartyService _partyService;
        private readonly IMobInfoProvider _mobInfoProvider;
        private readonly Func<uint> _currentTick;
        private readonly Dictionary<EntityID, MobRecord> _records = new Dictionary<EntityID, MobRecord>();

        /// <summary>Creates a party tag tracker.</summary>
        /// <param name="partyService">Resolves an attacker's party (Tier 1, ADR-010).</param>
        /// <param name="mobInfoProvider">Supplies a mob's MaxHP on its first recorded hit.</param>
        /// <param name="currentTick">Returns the current server tick (never wall-clock time).</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public PartyTagTracker(IPartyService partyService, IMobInfoProvider mobInfoProvider, Func<uint> currentTick)
        {
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _mobInfoProvider = mobInfoProvider ?? throw new ArgumentNullException(nameof(mobInfoProvider));
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        }

        /// <summary>
        /// F-LT-3: <c>ceil(maxHp x TAG_THRESHOLD_FRACTION)</c>, computed in exact integer arithmetic
        /// from <see cref="LootTableConstants.TAG_THRESHOLD_PERMILLE"/>. Never below
        /// <see cref="LootTableConstants.MIN_TAG_THRESHOLD"/>: a MaxHP below 1 (outside the GDD's
        /// [1, 9999] range) yields the minimum.
        /// </summary>
        /// <param name="maxHp">The mob's maximum HP.</param>
        public static int ComputeTagThreshold(int maxHp)
        {
            if (maxHp < 1)
            {
                return LootTableConstants.MIN_TAG_THRESHOLD;
            }

            // Integer ceiling of maxHp * permille / 1000; long so a huge MaxHP cannot overflow.
            long scaled = (long)maxHp * LootTableConstants.TAG_THRESHOLD_PERMILLE;
            return (int)((scaled + LootTableConstants.PERMILLE_DIVISOR - 1) / LootTableConstants.PERMILLE_DIVISOR);
        }

        /// <summary>
        /// Records one damage event (CR-LT-3). A zero hit is ignored silently. An unknown mob or an
        /// attacker without a party logs a server error and records nothing. Otherwise the damage is
        /// added to the attacker's party (saturating at <see cref="uint.MaxValue"/>) and the tag locks
        /// to that party if it reaches the threshold while no tag is locked (CR-LT-4).
        /// </summary>
        /// <param name="mobEntityId">The damaged mob.</param>
        /// <param name="attacker">The attacking character.</param>
        /// <param name="finalDamage">The final damage dealt by this event.</param>
        public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)
        {
            if (finalDamage == 0u)
            {
                return;
            }

            bool hasRecord = _records.TryGetValue(mobEntityId, out MobRecord record);
            MobInfo info = default;
            if (!hasRecord && !_mobInfoProvider.TryGetMob(mobEntityId, out info))
            {
                UnityEngine.Debug.LogError($"[PartyTagTracker] RecordDamage: {mobEntityId} is not a known mob; damage not recorded.");
                return;
            }

            PartyID party = _partyService.GetPartyID(attacker);
            if (party == PartyID.Uninitialized)
            {
                UnityEngine.Debug.LogError($"[PartyTagTracker] RecordDamage: {attacker} has no party (PartyID uninitialized); damage not recorded.");
                return;
            }

            // Created only after both guards pass, so a rejected hit leaves no state behind.
            if (!hasRecord)
            {
                record = CreateRecord(mobEntityId, info.MaxHP);
            }

            uint total = AddDamage(record, party, finalDamage);
            if (!record.IsLocked && total >= (uint)record.Threshold)
            {
                record.IsLocked = true;
                record.LockedOwner = party;
            }
        }

        /// <summary>
        /// Returns the mob's tag owner (CR-LT-4): the locked party if any; otherwise the fallback
        /// (highest cumulative damage, ties by earliest first-damage tick, a full tie by earliest
        /// recorded party). Returns <see langword="false"/> with
        /// <see cref="PartyID.Uninitialized"/> when the mob has no recorded damage.
        /// </summary>
        /// <param name="mobEntityId">The mob.</param>
        /// <param name="owner">The owning party, or <see cref="PartyID.Uninitialized"/>.</param>
        public bool TryGetTagOwner(EntityID mobEntityId, out PartyID owner)
        {
            owner = PartyID.Uninitialized;
            if (!_records.TryGetValue(mobEntityId, out MobRecord record) || record.Entries.Count == 0)
            {
                return false;
            }

            if (record.IsLocked)
            {
                owner = record.LockedOwner;
                return true;
            }

            owner = FindFallbackOwner(record);
            return true;
        }

        /// <summary>
        /// Drops the mob's damage record. An unknown mob is a silent no-op. Must be called on every
        /// path that removes a mob — kill resolution, leash reset, despawn — otherwise the record
        /// stays, and a reused <see cref="EntityID"/> would inherit its threshold and locked owner.
        /// </summary>
        /// <param name="mobEntityId">The mob to forget.</param>
        public void ClearMob(EntityID mobEntityId)
        {
            _records.Remove(mobEntityId);
        }

        /// <summary>Drops every mob's damage record. For zone teardown.</summary>
        public void Clear()
        {
            _records.Clear();
        }

        private MobRecord CreateRecord(EntityID mobEntityId, int maxHp)
        {
            if (maxHp < 1)
            {
                UnityEngine.Debug.LogError($"[PartyTagTracker] {mobEntityId} reports MaxHP={maxHp}; the tag threshold is clamped to {LootTableConstants.MIN_TAG_THRESHOLD}.");
            }

            var record = new MobRecord { Threshold = ComputeTagThreshold(maxHp) };
            _records[mobEntityId] = record;
            return record;
        }

        private uint AddDamage(MobRecord record, PartyID party, uint finalDamage)
        {
            List<PartyEntry> entries = record.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Party == party)
                {
                    PartyEntry updated = entries[i];
                    updated.CumulativeDamage = SaturatingAdd(updated.CumulativeDamage, finalDamage);
                    entries[i] = updated;
                    return updated.CumulativeDamage;
                }
            }

            entries.Add(new PartyEntry
            {
                Party = party,
                CumulativeDamage = finalDamage,
                FirstDamageTick = _currentTick(),
            });
            return finalDamage;
        }

        private static uint SaturatingAdd(uint current, uint amount)
        {
            return current > uint.MaxValue - amount ? uint.MaxValue : current + amount;
        }

        private static PartyID FindFallbackOwner(MobRecord record)
        {
            List<PartyEntry> entries = record.Entries;
            PartyEntry best = entries[0];
            for (int i = 1; i < entries.Count; i++)
            {
                PartyEntry candidate = entries[i];
                if (candidate.CumulativeDamage > best.CumulativeDamage
                    || (candidate.CumulativeDamage == best.CumulativeDamage
                        && candidate.FirstDamageTick < best.FirstDamageTick))
                {
                    best = candidate;
                }
            }
            return best.Party;
        }
    }
}
