using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// One authored mob row of the <see cref="MobRoster"/>; maps one-to-one onto <see cref="MobDefinition"/>
    /// (design/gdd/enemy-ai.md, MobDefinition Schema). Plain serialized fields; values are checked at load by
    /// <see cref="MobDefinitionValidator"/>, not here.
    /// </summary>
    /// <remarks>
    /// The public fields are deliberate: they are serialized row data whose names are the YAML keys of the roster asset,
    /// so renaming them or wrapping them in properties would break the authored asset.
    /// </remarks>
    [Serializable]
    public sealed class MobRosterEntry
    {
        /// <summary>Raw <see cref="MobTypeID"/> value; 0 is invalid.</summary>
        public uint MobTypeId;
        /// <summary>Display name.</summary>
        public string DisplayName;
        /// <summary>Mob tier.</summary>
        public MobTier MobTier;
        /// <summary>Base maximum hit points.</summary>
        public int BaseMaxHP;
        /// <summary>Base attack power.</summary>
        public int BaseAttackPower;
        /// <summary>Base defense.</summary>
        public int BaseDefense;
        /// <summary>Attack reach in metres.</summary>
        public float AttackRange;
        /// <summary>Move speed in metres per second.</summary>
        public float MoveSpeed;
        /// <summary>Aggro radius in metres.</summary>
        public float AggroRange;
        /// <summary>Leash radius in metres.</summary>
        public float LeashRange;
        /// <summary>Attack wind-up in ticks.</summary>
        public int AttackWindUpTicks;
        /// <summary>Attack recovery in ticks.</summary>
        public int AttackRecoveryTicks;
        /// <summary>Attack cooldown in ticks.</summary>
        public int AttackCooldownTicks;
        /// <summary>XP awarded on kill.</summary>
        public int KillXP;
        /// <summary>Chance a spawn is Enraged.</summary>
        public float EnragedChance = 0.07f;
        /// <summary>Enraged max HP multiplier.</summary>
        public float EnragedMaxHPMultiplier = 1.5f;
        /// <summary>Enraged attack power multiplier.</summary>
        public float EnragedAttackPowerMultiplier = 1.3f;
        /// <summary>Enraged XP multiplier.</summary>
        public float EnragedXPMultiplier = 1.5f;
        /// <summary>Enraged loot quality bonus tier.</summary>
        public int EnragedLootQualityBonusTier = 1;

        /// <summary>Builds the immutable definition from this row.</summary>
        /// <returns>The definition, unvalidated.</returns>
        public MobDefinition ToDefinition()
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(MobTypeId),
                displayName: DisplayName,
                baseMaxHP: BaseMaxHP,
                baseAttackPower: BaseAttackPower,
                baseDefense: BaseDefense,
                attackRange: AttackRange,
                moveSpeed: MoveSpeed,
                aggroRange: AggroRange,
                leashRange: LeashRange,
                attackWindUpTicks: AttackWindUpTicks,
                attackRecoveryTicks: AttackRecoveryTicks,
                attackCooldownTicks: AttackCooldownTicks,
                killXP: KillXP,
                mobTier: MobTier,
                enragedChance: EnragedChance,
                enragedMaxHPMultiplier: EnragedMaxHPMultiplier,
                enragedAttackPowerMultiplier: EnragedAttackPowerMultiplier,
                enragedXPMultiplier: EnragedXPMultiplier,
                enragedLootQualityBonusTier: EnragedLootQualityBonusTier);
        }
    }

    /// <summary>
    /// The mob roster asset (Enemy AI Story 002): the authored <see cref="MobRosterEntry"/> rows the zone server turns
    /// into the <see cref="IMobDefinitionRegistry"/> at startup S2 (<c>ZoneMobDataLoader</c>).
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    /// <example>
    /// <code>
    /// IReadOnlyList&lt;MobDefinition&gt; definitions = roster.ToDefinitions();
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "MobRoster", menuName = "IronGrind/Mob Roster")]
    public class MobRoster : ScriptableObject
    {
        [SerializeField] private List<MobRosterEntry> _entries = new List<MobRosterEntry>();

        /// <summary>Number of authored rows.</summary>
        public int Count => _entries == null ? 0 : _entries.Count;

        /// <summary>Maps every row to a <see cref="MobDefinition"/>, in authored order, without validating.</summary>
        /// <returns>The definitions; empty when there are no rows.</returns>
        public IReadOnlyList<MobDefinition> ToDefinitions()
        {
            var result = new List<MobDefinition>(Count);
            for (int i = 0; i < Count; i++)
            {
                if (_entries[i] == null)
                {
                    Debug.LogWarning("[MobRoster] row " + i.ToString(CultureInfo.InvariantCulture) + " is null; skipped");
                    continue;
                }

                result.Add(_entries[i].ToDefinition());
            }

            return result;
        }

        /// <summary>Replaces the rows; test seam only.</summary>
        internal void SetEntriesForTesting(IEnumerable<MobRosterEntry> entries)
        {
            _entries = new List<MobRosterEntry>(entries);
        }
    }
}
