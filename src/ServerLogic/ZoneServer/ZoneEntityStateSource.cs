using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.EquipmentSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>Narrow read view over Character Stats for <see cref="ZoneEntityStateSource"/>.</summary>
    public interface IZoneEntityStatReader
    {
        /// <summary>Current hit points of the entity.</summary>
        /// <param name="entityId">The entity.</param>
        /// <returns>The current HP.</returns>
        float GetCurrentHP(EntityID entityId);

        /// <summary>Current mana of the entity.</summary>
        /// <param name="entityId">The entity.</param>
        /// <returns>The current MP.</returns>
        float GetCurrentMP(EntityID entityId);

        /// <summary>Effective value of a stat.</summary>
        /// <param name="entityId">The entity.</param>
        /// <param name="statId">The stat.</param>
        /// <returns>The effective value.</returns>
        int GetEffectiveStat(EntityID entityId, StatID statId);
    }

    /// <summary>Narrow read view over Auto-Attack Combat for <see cref="ZoneEntityStateSource"/>.</summary>
    public interface IZoneEntityCombatReader
    {
        /// <summary>The entity's auto-attack state.</summary>
        /// <param name="entityId">The entity.</param>
        /// <returns>The state.</returns>
        AutoAttackState GetState(EntityID entityId);

        /// <summary>The action timer in ticks.</summary>
        /// <param name="entityId">The entity.</param>
        /// <returns>The timer.</returns>
        int GetActionTimer(EntityID entityId);

        /// <summary>The sampled cycle length in ticks.</summary>
        /// <param name="entityId">The entity.</param>
        /// <returns>The cycle length.</returns>
        int GetCycleTicks(EntityID entityId);
    }

    /// <summary>Adapter: <see cref="IZoneEntityStatReader"/> over the zone's Character Stats.</summary>
    public sealed class CharacterStatsZoneEntityStatReader : IZoneEntityStatReader
    {
        private readonly ZoneStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">Character Stats.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsZoneEntityStatReader(ZoneStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public float GetCurrentHP(EntityID entityId) => _stats.GetCurrentHP(entityId);

        /// <inheritdoc/>
        public float GetCurrentMP(EntityID entityId) => _stats.GetCurrentMP(entityId);

        /// <inheritdoc/>
        public int GetEffectiveStat(EntityID entityId, StatID statId) => _stats.GetEffectiveStat(entityId, statId);
    }

    /// <summary>Adapter: <see cref="IZoneEntityCombatReader"/> over <see cref="AutoAttackCombatSystem"/>.</summary>
    public sealed class AutoAttackZoneEntityCombatReader : IZoneEntityCombatReader
    {
        private readonly AutoAttackCombatSystem _combat;

        /// <summary>Creates the adapter.</summary>
        /// <param name="combat">The auto-attack system.</param>
        /// <exception cref="ArgumentNullException"><paramref name="combat"/> is null.</exception>
        public AutoAttackZoneEntityCombatReader(AutoAttackCombatSystem combat)
        {
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        }

        /// <inheritdoc/>
        public AutoAttackState GetState(EntityID entityId) => _combat.GetState(entityId);

        /// <inheritdoc/>
        public int GetActionTimer(EntityID entityId) => _combat.GetActionTimer(entityId);

        /// <inheritdoc/>
        public int GetCycleTicks(EntityID entityId) => _combat.GetCycleTicks(entityId);
    }

    /// <summary>
    /// Production <see cref="IZoneEntityStateSource"/> (Zone Instancing Story 015, TD-100): reads each snapshot and
    /// <c>PlayerJoinedZone</c> field from the system that owns it. Position, rotation and facing stay zero for
    /// <c>MovementZoneEntityStateSource</c> to overwrite. Main thread only; allocation-free per call.
    /// </summary>
    /// <remarks>
    /// The real <see cref="AutoAttackCombatSystem"/> resolves only player slots, so a mob reads Idle (IsInCombat false,
    /// CycleTimer 0) unless the combat reader is a <c>MobAwareZoneEntityCombatReader</c> (Enemy AI Story 012; wired in
    /// Story 013), which supplies mob <c>isInCombat</c> from Enemy AI. Mob stats are seeded on spawn by
    /// <c>MobRuntimeStore</c> once wired in Story 013 (TD-102); until then a mob reads 0 / 0 HP. A mob's position and
    /// facing come from the optional <see cref="IZoneMobTransformReader"/>; without it (or for a mob it does not hold)
    /// they are zero. RespawnTick comes from the optional <see cref="IPlayerDeathStateReader"/> (Death and Respawn
    /// Story 007, EC-DR-1): the dead player's <c>DeathState.RespawnTick</c>, clamped to <see cref="uint.MaxValue"/>
    /// with one error log per clamp; 0 when the player is alive or there is no reader.
    /// </remarks>
    public sealed class ZoneEntityStateSource : IZoneEntityStateSource
    {
        private const float ROTATION_SCALE = 32767f;

        private readonly IZoneMobTransformReader _mobTransforms;
        private readonly ZoneSlotPool _pool;
        private readonly IZoneMobRoster _mobs;
        private readonly IZoneEntityStatReader _stats;
        private readonly ICharacterNameLookup _names;
        private readonly IEquipmentService _equipment;
        private readonly IZoneEntityCombatReader _combat;
        private readonly IPlayerDeathStateReader _deathStates;

        /// <summary>Creates the source.</summary>
        /// <param name="pool">Player slot pool.</param>
        /// <param name="mobs">Mob roster.</param>
        /// <param name="stats">Character Stats view.</param>
        /// <param name="names">Character name lookup.</param>
        /// <param name="equipment">Equipment service.</param>
        /// <param name="combat">Auto-attack view.</param>
        /// <param name="mobTransforms">Optional committed mob transform reader (Enemy AI Story 008); null leaves mob
        /// position, rotation and facing at zero.</param>
        /// <param name="deathStates">
        /// Optional dead-ness reader (Death and Respawn Story 007); null gives RespawnTick 0.
        /// </param>
        /// <exception cref="ArgumentNullException">Any argument other than <paramref name="mobTransforms"/> or
        /// <paramref name="deathStates"/> is null.</exception>
        public ZoneEntityStateSource(ZoneSlotPool pool, IZoneMobRoster mobs, IZoneEntityStatReader stats,
            ICharacterNameLookup names, IEquipmentService equipment, IZoneEntityCombatReader combat,
            IZoneMobTransformReader mobTransforms = null, IPlayerDeathStateReader deathStates = null)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _mobs = mobs ?? throw new ArgumentNullException(nameof(mobs));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _names = names ?? throw new ArgumentNullException(nameof(names));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _mobTransforms = mobTransforms;
            _deathStates = deathStates;
        }

        /// <inheritdoc/>
        public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
        {
            view = default;
            if (!_pool.TryGetPlayer(id, out ZonePlayerSlot slot))
            {
                return false;
            }

            if (!_names.TryGetCharacterName(slot.Character, out string name) || string.IsNullOrEmpty(name))
            {
                return false;
            }

            // posX, posY, posZ, rotX, rotY, rotZ, rotW, facingAngle = 0: MovementZoneEntityStateSource overwrites them.
            view = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0,
                Mathf.FloorToInt(_stats.GetCurrentHP(id)),
                _stats.GetEffectiveStat(id, StatID.MaxHP),
                Mathf.FloorToInt(_stats.GetCurrentMP(id)),
                _stats.GetEffectiveStat(id, StatID.MaxMP),
                _stats.GetEffectiveStat(id, StatID.Level),
                IsEngaged(id),
                CycleTimerBroadcastStep.NormalizeCycleTimer(_combat.GetActionTimer(id), _combat.GetCycleTicks(id)),
                _equipment.GetAppearanceFlags(slot.Character),
                name,
                ReadRespawnTick(id));
            return true;
        }

        /// <inheritdoc/>
        public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
        {
            view = default;
            if (!_mobs.TryGetMob(id, out MobTypeID _, out Vector3 _))
            {
                return false;
            }

            // posX, posY, posZ, rotX, rotY, rotZ, rotW, facingAngle = 0 (rule 8; the Movement decorator passes mobs through).
            short px = 0, py = 0, pz = 0, rx = 0, ry = 0, rz = 0, rw = 0, face = 0;
            if (_mobTransforms != null && _mobTransforms.TryGetTransform(id, out Vector3 position, out short facing))
            {
                Quaternion yaw = Quaternion.Euler(0f, facing / 10f, 0f);
                px = ToWire(position.x);
                py = ToWire(position.y);
                pz = ToWire(position.z);
                rx = (short)Mathf.RoundToInt(yaw.x * ROTATION_SCALE);
                ry = (short)Mathf.RoundToInt(yaw.y * ROTATION_SCALE);
                rz = (short)Mathf.RoundToInt(yaw.z * ROTATION_SCALE);
                rw = (short)Mathf.RoundToInt(yaw.w * ROTATION_SCALE);
                face = facing;
            }

            view = new ZoneMobStateView(px, py, pz, rx, ry, rz, rw, face,
                Mathf.FloorToInt(_stats.GetCurrentHP(id)),
                _stats.GetEffectiveStat(id, StatID.MaxHP),
                IsEngaged(id),
                CycleTimerBroadcastStep.NormalizeCycleTimer(_combat.GetActionTimer(id), _combat.GetCycleTicks(id)));
            return true;
        }

        // Death and Respawn Story 007 AC-3: DeathState.RespawnTick (long) into the view's uint; 0 when alive, not
        // positive, or no reader.
        private uint ReadRespawnTick(EntityID id)
        {
            if (_deathStates == null || !_deathStates.TryGetRespawnTick(id, out long respawnTick) || respawnTick <= 0L)
            {
                return 0u;
            }

            if (respawnTick > uint.MaxValue)
            {
                Debug.LogError($"[ZoneEntityStateSource] RespawnTick {respawnTick} of {id} does not fit the uint "
                    + $"snapshot field; clamped to {uint.MaxValue}.");
                return uint.MaxValue;
            }

            return (uint)respawnTick;
        }

        // Same clamp as WireFixedPointCodec.EncodePosition (centimetres), silent.
        private static short ToWire(float meters)
        {
            float clamped = Mathf.Clamp(meters, -WireFixedPointCodec.MaxPositionMeters, WireFixedPointCodec.MaxPositionMeters);
            return (short)Mathf.RoundToInt(clamped * 100f);
        }

        private bool IsEngaged(EntityID id)
        {
            AutoAttackState state = _combat.GetState(id);
            return state == AutoAttackState.CombatActive || state == AutoAttackState.OutOfRange;
        }
    }
}
