using System;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Navigation's mob speed source (CR-NAV-5) over the mob definition registry (Enemy AI Story 013): a known mob type
    /// moves at its definition's <see cref="MobDefinition.MoveSpeed"/>. An unknown type, and
    /// <see cref="MobTypeID.Invalid"/>, are not found (the registry returns no definition for them). Replaces the
    /// constant-speed stand-in and its config knob. Never logs.
    /// </summary>
    /// <example>
    /// <code>
    /// var source = new MobDefinitionRegistryNavSpeedSource(registry);
    /// bool known = source.TryGetMoveSpeed(new MobTypeID(1u), out float speed);   // true, definition.MoveSpeed
    /// bool unknown = source.TryGetMoveSpeed(MobTypeID.Invalid, out _);           // false
    /// </code>
    /// </example>
    public sealed class MobDefinitionRegistryNavSpeedSource : INavMobSpeedSource
    {
        private readonly IMobDefinitionRegistry _registry;

        /// <summary>Creates the source over a registry.</summary>
        /// <param name="registry">The mob definition registry; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
        public MobDefinitionRegistryNavSpeedSource(IMobDefinitionRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public bool TryGetMoveSpeed(MobTypeID mobType, out float speed)
        {
            MobDefinition definition = _registry.GetDefinition(mobType);
            if (definition == null)
            {
                speed = 0f;
                return false;
            }

            speed = definition.MoveSpeed;
            return true;
        }
    }
}
