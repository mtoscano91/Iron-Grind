using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using UnityEngine;
using UnityEngine.AI;
using CharacterID = IronGrind.Currency.CharacterID;
using MobTypeID = IronGrind.LootTableSystem.MobTypeID;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Logs "[ZoneServer] &lt;step&gt; not implemented" the first time it is asked, then stays silent.
    /// One instance per stand-in, so the log is once per stand-in instance across all its methods.
    /// </summary>
    internal sealed class StandInLogOnce
    {
        private readonly string _step;
        private bool _logged;

        public StandInLogOnce(string step)
        {
            _step = step;
        }

        public void Log()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            Debug.Log("[ZoneServer] " + _step + " not implemented");
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IConnectionCharacterDirectory"/> until the session epic ships: finds no character for
    /// any connection, so the dispatcher drops every request on its <c>NoCharacter</c> path. Logs
    /// "[ZoneServer] connection character directory not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// bool found = new StandInConnectionCharacterDirectory().TryGetCharacterId(5u, out CharacterID id); // false
    /// </code>
    /// </example>
    public sealed class StandInConnectionCharacterDirectory : IConnectionCharacterDirectory
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("connection character directory");

        /// <inheritdoc/>
        public bool TryGetCharacterId(uint clientId, out IronGrind.Currency.CharacterID charId)
        {
            _log.Log();
            charId = IronGrind.Currency.CharacterID.Invalid;
            return false;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IConnectionActivitySink"/> until the session epic ships: records nothing. Logs
    /// "[ZoneServer] connection activity sink not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInConnectionActivitySink().OnInboundActivity(5u, 100u); // logs once, records nothing
    /// </code>
    /// </example>
    public sealed class StandInConnectionActivitySink : IConnectionActivitySink
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("connection activity sink");

        /// <inheritdoc/>
        public void OnInboundActivity(uint clientId, uint serverTick)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneNavigation"/> until the Navigation epic ships (ADR-009 A1 Decision 2).
    /// Logs "[ZoneServer] navigation not implemented" once and does nothing else.
    /// </summary>
    /// <example>
    /// <code>
    /// var nav = new StandInZoneNavigation();
    /// nav.Teardown(); // logs once
    /// </code>
    /// </example>
    public sealed class StandInZoneNavigation : IZoneNavigation
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("navigation");

        /// <inheritdoc/>
        public void Initialize(ZoneID zone, NavMeshDataInstance instance)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public void Teardown()
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneBoundsValidator"/> (no longer used by the host, which uses ZoneDefinitionBoundsValidator): logs "[ZoneServer] bounds validation not implemented"
    /// once and always returns true.
    /// </summary>
    /// <example>
    /// <code>
    /// bool ok = new StandInZoneBoundsValidator().Validate(zone); // true
    /// </code>
    /// </example>
    public sealed class StandInZoneBoundsValidator : IZoneBoundsValidator
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("bounds validation");

        /// <inheritdoc/>
        public bool Validate(ZoneID zone)
        {
            _log.Log();
            return true;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IGatewayRegistration"/>: logs "[ZoneServer] gateway registration not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInGatewayRegistration().RegisterZone(zone, 7777);
    /// </code>
    /// </example>
    public sealed class StandInGatewayRegistration : IGatewayRegistration
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("gateway registration");

        /// <inheritdoc/>
        public void RegisterZone(ZoneID zone, ushort port)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneCloseSteps"/>: logs "[ZoneServer] zone close steps not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneCloseSteps().RunCloseSteps();
    /// </code>
    /// </example>
    public sealed class StandInZoneCloseSteps : IZoneCloseSteps
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("zone close steps");

        /// <inheritdoc/>
        public void RunCloseSteps()
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneStateOwner"/>: logs "[ZoneServer] zone state not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneStateOwner().MarkActive(zone);
    /// </code>
    /// </example>
    public sealed class StandInZoneStateOwner : IZoneStateOwner
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("zone state");

        /// <inheritdoc/>
        public void MarkActive(ZoneID zone)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public void MarkClosedInvalidBounds(ZoneID zone)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IAutoAttackTargetValidity"/> until Enemy AI ships (Auto-Attack Combat Story 006): a target
    /// is valid when it holds a mob slot of the zone's pool. "Living" is not known yet; players are never valid.
    /// </summary>
    /// <example>
    /// <code>
    /// bool valid = new StandInAutoAttackTargetValidity(pool).IsValidTarget(mobId);
    /// </code>
    /// </example>
    public sealed class StandInAutoAttackTargetValidity : IAutoAttackTargetValidity
    {
        private readonly ZoneSlotPool _pool;

        /// <summary>Creates the stand-in over the zone's slot pool.</summary>
        /// <param name="pool">The zone's pool; not null.</param>
        public StandInAutoAttackTargetValidity(ZoneSlotPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <inheritdoc/>
        public bool IsValidTarget(EntityID target)
        {
            return _pool.TryGetMobSlot(target, out _);
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IAutoAttackDamageCalculator"/> until the item database and equipment are wired into the
    /// zone (Auto-Attack Combat Story 006): rejects every hit, so nothing is applied. Logs
    /// "[ZoneServer] auto-attack damage calculation not implemented" once.
    /// </summary>
    public sealed class StandInAutoAttackDamageCalculator : IAutoAttackDamageCalculator
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("auto-attack damage calculation");

        /// <inheritdoc/>
        public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
        {
            _log.Log();
            return DamageResult.Rejected(DamageContext.PhysicalAuto);
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IAutoAttackDamageApplier"/> until Enemy AI owns mob health (Auto-Attack Combat Story 006):
    /// applies nothing. Logs "[ZoneServer] auto-attack damage applier not implemented" once.
    /// </summary>
    public sealed class StandInAutoAttackDamageApplier : IAutoAttackDamageApplier
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("auto-attack damage applier");

        /// <inheritdoc/>
        public void Apply(EntityID attacker, EntityID target, in DamageResult result)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Placeholder <see cref="IAutoAttackXpAward"/> until Leveling and Enemy AI ship (Auto-Attack Combat Story 006):
    /// every mob is worth 0 XP.
    /// </summary>
    public sealed class StandInAutoAttackXpAward : IAutoAttackXpAward
    {
        /// <summary>The XP of every mob until Leveling and Enemy AI exist.</summary>
        public const int PLACEHOLDER_XP = 0;

        /// <inheritdoc/>
        public int GetXPAward(EntityID target)
        {
            return PLACEHOLDER_XP;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IAutoAttackMobTypeSource"/> (Auto-Attack Combat Story 006): knows no mob type. The
    /// roster-backed <c>ZoneMobRosterMobTypeSource</c> needs the zone's <c>IZoneMobRoster</c>, which the Zone Instancing
    /// wiring does not expose yet.
    /// </summary>
    public sealed class StandInAutoAttackMobTypeSource : IAutoAttackMobTypeSource
    {
        /// <inheritdoc/>
        public bool TryGetMobType(EntityID mob, out MobTypeID type)
        {
            type = default;
            return false;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneStaticDataLoader"/>: logs "[ZoneServer] static data loading not implemented"
    /// once and returns true.
    /// </summary>
    /// <example>
    /// <code>
    /// bool ok = new StandInZoneStaticDataLoader().LoadAndValidate(); // true
    /// </code>
    /// </example>
    public sealed class StandInZoneStaticDataLoader : IZoneStaticDataLoader
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("static data loading");

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            _log.Log();
            return true;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="ISkillDamageCalculator"/> until the item database and equipment are wired into the zone
    /// (Skill System Story 011; the real <see cref="DamageCalculatorSkillAdapter"/> needs them): rejects every hit, so
    /// nothing is applied. Logs "[ZoneServer] skill damage calculation not implemented" once.
    /// </summary>
    public sealed class StandInSkillDamageCalculator : ISkillDamageCalculator
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("skill damage calculation");

        /// <inheritdoc/>
        public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
        {
            _log.Log();
            return DamageResult.Rejected(context);
        }
    }
}
