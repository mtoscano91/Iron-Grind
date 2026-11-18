using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using UnityEngine;
using EntityDiedHandler = IronGrind.CharacterStats.CharacterStats.EntityDiedHandler;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// A real <see cref="DeathAndRespawnService"/> over a caller's pool with inert seams, for tests in other systems
    /// that need the production <see cref="IPlayerDeathStateReader"/> (Death and Respawn Story 007). Default config
    /// (delay 140 ticks); the town point always resolves. The caller disposes <see cref="Service"/>.
    /// </summary>
    internal sealed class RealDeathRig
    {
        /// <summary>The tick <see cref="Kill"/> records as the death tick.</summary>
        public const long DEATH_TICK = 1000L;

        private sealed class DeathSource : IEntityDeathSource
        {
            private readonly List<EntityDiedHandler> _handlers = new List<EntityDiedHandler>();

            public void Subscribe(EntityDiedHandler handler) { _handlers.Add(handler); }

            public void Unsubscribe(EntityDiedHandler handler) { _handlers.Remove(handler); }

            public void Raise(EntityID entity)
            {
                for (int i = 0; i < _handlers.Count; i++)
                {
                    _handlers[i](entity);
                }
            }
        }

        private sealed class Ticks : IDeathTickSource
        {
            public long Value = DEATH_TICK;

            public long CurrentTick => Value;
        }

        private sealed class NoCombat : ICombatProhibitionSink
        {
            public void SetCombatProhibited(EntityID entity, bool prohibited) { }
        }

        private sealed class NoMovement : IMovementSystem
        {
            public bool SetProhibited(EntityID id, bool prohibited) { return true; }

            public bool InjectFacingOverride(EntityID id, Vector3 direction) { return true; }
        }

        private sealed class NoGhostSink : IGhostDeathSink
        {
            public void OnGhostDied(EntityID entity) { }
        }

        private readonly DeathSource _source = new DeathSource();
        private readonly Ticks _ticks = new Ticks();

        /// <summary>The real service.</summary>
        public readonly DeathAndRespawnService Service;

        /// <summary>Builds the service over <paramref name="pool"/>.</summary>
        public RealDeathRig(ZoneSlotPool pool)
        {
            Service = new DeathAndRespawnService(DeathAndRespawnConfigValues.Defaults, pool, _source, _ticks,
                new NoCombat(), new NoMovement(), new NoOpPartyMemberStatusSink(), new ZoneSlotGhostStatusSource(pool),
                new NoGhostSink(), new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(),
                new RespawnStubs.Status(), new RespawnStubs.Output());
        }

        /// <summary>Raises a death of <paramref name="id"/> at <paramref name="tick"/>.</summary>
        public void Kill(EntityID id, long tick = DEATH_TICK)
        {
            _ticks.Value = tick;
            _source.Raise(id);
        }
    }
}
