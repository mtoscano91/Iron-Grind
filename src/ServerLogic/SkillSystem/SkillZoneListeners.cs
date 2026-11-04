using System;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Entry listener that spawns the player's skill store (Skill System Story 011). Reads the class type from
    /// <see cref="LevelingService.TryGetClassType"/>; an unknown class logs one warning per instance and skips the spawn.
    /// </summary>
    public sealed class SkillSpawnEntryListener : IZonePlayerEntryListener
    {
        private readonly SkillSystemService _skills;
        private readonly LevelingService _leveling;
        private bool _warned;

        /// <summary>Creates the listener.</summary>
        /// <param name="skills">The skill service.</param>
        /// <param name="leveling">The class-type source.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SkillSpawnEntryListener(SkillSystemService skills, LevelingService leveling)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _leveling = leveling ?? throw new ArgumentNullException(nameof(leveling));
        }

        /// <inheritdoc/>
        public void OnPlayerEntering(byte slot, EntityID id)
        {
            if (!_leveling.TryGetClassType(id, out byte classType))
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[SkillSystem] no class type for entering player " + id.RawValue + "; skill store not created");
                }

                return;
            }

            _skills.OnEntitySpawned(id, (ClassType)classType);
        }
    }

    /// <summary>Slot release listener that removes the player's skill store (Skill System Story 011).</summary>
    public sealed class SkillSlotReleaseListener : IZoneSlotReleaseListener
    {
        private readonly SkillSystemService _skills;

        /// <summary>Creates the listener.</summary>
        /// <param name="skills">The skill service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="skills"/> is null.</exception>
        public SkillSlotReleaseListener(SkillSystemService skills)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        /// <inheritdoc/>
        public void BeforeRelease(EntityID id)
        {
            _skills.OnEntityDespawned(id);
        }
    }

    /// <summary>Fixed <see cref="ISkillSystemConfigSource"/> over one <see cref="SkillSystemConfigValues"/>.</summary>
    public sealed class StaticSkillSystemConfigSource : ISkillSystemConfigSource
    {
        /// <summary>Creates the source.</summary>
        /// <param name="values">The values to serve.</param>
        public StaticSkillSystemConfigSource(SkillSystemConfigValues values)
        {
            Values = values;
        }

        /// <inheritdoc/>
        public SkillSystemConfigValues Values { get; }
    }

    /// <summary>
    /// Dispose pass entry for the Skill System (Story 011): removes the level-up subscription from Leveling and
    /// destroys the fallback roster when the composition created one.
    /// </summary>
    internal sealed class SkillSystemTeardown : IDisposable
    {
        private readonly SkillSystemService _service;
        private readonly LevelingService _leveling;
        private readonly SkillDatabase _ownedDatabase;

        /// <summary>Creates the teardown.</summary>
        /// <param name="service">The skill service to unsubscribe.</param>
        /// <param name="leveling">The Leveling service it subscribed to.</param>
        /// <param name="ownedDatabase">The roster to destroy, or null when the roster was supplied.</param>
        public SkillSystemTeardown(SkillSystemService service, LevelingService leveling, SkillDatabase ownedDatabase)
        {
            _service = service;
            _leveling = leveling;
            _ownedDatabase = ownedDatabase;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _service.Unsubscribe(_leveling);
            if (_ownedDatabase != null)
            {
                UnityEngine.Object.DestroyImmediate(_ownedDatabase);
            }
        }
    }
}
