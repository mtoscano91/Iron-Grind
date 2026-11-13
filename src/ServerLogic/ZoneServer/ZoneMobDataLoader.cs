using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.SkillSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The mob definition registry loaded at startup S2 (Enemy AI Story 002). The composition looks for it on its
    /// <see cref="IZoneStaticDataLoader"/> (consumed from Enemy AI Story 013).
    /// </summary>
    public interface IZoneMobData
    {
        /// <summary>The registry of accepted mob definitions; null until <c>LoadAndValidate</c> succeeded.</summary>
        IMobDefinitionRegistry LoadedMobRegistry { get; }
    }

    /// <summary>
    /// Startup S2 loader for the mob roster (Enemy AI Story 002; ADR-009 Amendment 1 S2; enemy-ai.md CR-AI-7). Builds the
    /// registry through <see cref="MobDefinitionRegistry.Build"/> (refused rows are logged there and are not fatal), then
    /// runs the loader for the remaining static data. A null roster, an empty roster or a registry with no accepted
    /// definition logs one <c>[ZoneServer] S2: &lt;reason&gt;</c> error and returns false.
    /// </summary>
    /// <remarks>
    /// Lives in <c>ZoneServer/</c> beside the other S2 loaders. It is the outermost loader of the chain, so it forwards
    /// <see cref="IZoneClassSkillData"/> and <see cref="IZoneItemData"/> from <c>rest</c>: the composition finds both by
    /// type on the outermost loader and would otherwise fall back to empty data at S5.
    /// </remarks>
    /// <example>
    /// <code>
    /// var loader = new ZoneMobDataLoader(roster, new StandInZoneStaticDataLoader());
    /// if (!loader.LoadAndValidate()) { /* S-fail */ }
    /// IMobDefinitionRegistry mobs = loader.LoadedMobRegistry;
    /// </code>
    /// </example>
    public sealed class ZoneMobDataLoader : IZoneStaticDataLoader, IZoneMobData, IZoneClassSkillData, IZoneItemData
    {
        private const string LOG_PREFIX = "[ZoneServer] S2: ";

        private readonly MobRoster _roster;
        private readonly IZoneStaticDataLoader _rest;

        /// <summary>Creates the loader.</summary>
        /// <param name="roster">The mob roster asset; null or empty fails S2.</param>
        /// <param name="rest">The loader for the other static data, run after this one succeeds; null for none.</param>
        public ZoneMobDataLoader(MobRoster roster, IZoneStaticDataLoader rest)
        {
            _roster = roster;
            _rest = rest;
        }

        /// <inheritdoc/>
        public IMobDefinitionRegistry LoadedMobRegistry { get; private set; }

        /// <summary>The class registry of the <c>rest</c> loader, forwarded; null until success or when it has none.</summary>
        public ClassRegistry LoadedClassRegistry { get; private set; }

        /// <summary>The skill roster of the <c>rest</c> loader, forwarded; null until success or when it has none.</summary>
        public SkillDatabase LoadedSkillDatabase { get; private set; }

        /// <summary>The item database of the <c>rest</c> loader, forwarded; null until success or when it has none.</summary>
        public IItemDatabase LoadedItemDatabase { get; private set; }

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            // A failed reload must not leave the data of an earlier success exposed.
            LoadedMobRegistry = null;
            LoadedClassRegistry = null;
            LoadedSkillDatabase = null;
            LoadedItemDatabase = null;

            if (_roster == null)
            {
                Debug.LogError(LOG_PREFIX + "MobRoster missing");
                return false;
            }

            if (_roster.Count == 0)
            {
                Debug.LogError(LOG_PREFIX + "MobRoster has no mobs");
                return false;
            }

            MobDefinitionRegistry registry = MobDefinitionRegistry.Build(_roster.ToDefinitions());
            if (registry.Count == 0)
            {
                Debug.LogError(LOG_PREFIX + "MobRoster has no valid mob definition");
                return false;
            }

            if (_rest != null && !_rest.LoadAndValidate())
            {
                return false;
            }

            LoadedMobRegistry = registry;
            LoadedClassRegistry = (_rest as IZoneClassSkillData)?.LoadedClassRegistry;
            LoadedSkillDatabase = (_rest as IZoneClassSkillData)?.LoadedSkillDatabase;
            LoadedItemDatabase = (_rest as IZoneItemData)?.LoadedItemDatabase;
            return true;
        }
    }
}
