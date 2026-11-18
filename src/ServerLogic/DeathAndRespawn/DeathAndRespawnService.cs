using System;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using UnityEngine;
using EntityDiedHandler = IronGrind.CharacterStats.CharacterStats.EntityDiedHandler;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Death entry (death-and-respawn.md CR-DR-1, CR-DR-2, CR-DR-3, CR-DR-5, CR-DR-6, T-1, EC-DR-6; Death and Respawn
    /// Story 002, ADR-010). Subscribes once to the entity-death source; a player death sets Dead, stamps the death and
    /// respawn ticks, marks the pool slot Dead, and bans combat and movement. A ghost death (Story 003, CR-DR-Ghost)
    /// is checked first and handed to <see cref="IGhostDeathSink"/> with no Dead state. Respawn (Story 004) and wire
    /// messages (Story 005) are not here. Tick thread only; allocates nothing per death.
    /// </summary>
    public sealed class DeathAndRespawnService : IDisposable
    {
        private const string LOG_PREFIX = "[DeathAndRespawn] ";

        private readonly DeathAndRespawnConfigValues _config;
        private readonly ZoneSlotPool _pool;
        private readonly IEntityDeathSource _deathSource;
        private readonly IDeathTickSource _ticks;
        private readonly ICombatProhibitionSink _combat;
        private readonly IMovementSystem _movement;
        private readonly IPartyMemberStatusSink _party;
        private readonly IGhostStatusSource _ghosts;
        private readonly IGhostDeathSink _ghostDeaths;
        private readonly DeathState[] _states;
        private readonly EntityDiedHandler _deathHandler;
        private bool _disposed;

        /// <summary>Creates the service and subscribes to <paramref name="deathSource"/> once.</summary>
        /// <param name="config">
        /// Config values (RESPAWN_DELAY_TICKS); must pass <see cref="DeathAndRespawnConfigValues.Validate"/>.
        /// </param>
        /// <param name="pool">The zone slot pool.</param>
        /// <param name="deathSource">Entity-death notification seam.</param>
        /// <param name="ticks">Current tick source.</param>
        /// <param name="combat">Combat prohibition seam (CR-DR-3).</param>
        /// <param name="movement">Movement prohibition seam (CR-DR-3).</param>
        /// <param name="party">Party status seam (CR-DR-5).</param>
        /// <param name="ghosts">Ghost status seam (CR-DR-Ghost).</param>
        /// <param name="ghostDeaths">Ghost death hand-off seam (Story 010 adapter).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="config"/> fails validation (errors logged; EC-DR-5:
        /// reject, never clamp).</exception>
        public DeathAndRespawnService(DeathAndRespawnConfigValues config, ZoneSlotPool pool,
            IEntityDeathSource deathSource, IDeathTickSource ticks, ICombatProhibitionSink combat,
            IMovementSystem movement, IPartyMemberStatusSink party,
            IGhostStatusSource ghosts, IGhostDeathSink ghostDeaths)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _deathSource = deathSource ?? throw new ArgumentNullException(nameof(deathSource));
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _ghosts = ghosts ?? throw new ArgumentNullException(nameof(ghosts));
            _ghostDeaths = ghostDeaths ?? throw new ArgumentNullException(nameof(ghostDeaths));
            if (!config.Validate())
            {
                throw new ArgumentException("DeathAndRespawnConfigValues failed validation (see the logged errors).",
                    nameof(config));
            }

            _config = config;
            _states = new DeathState[pool.MaxPlayers];

            // One delegate instance, kept so Unsubscribe matches by reference (ADR-010).
            _deathHandler = OnEntityDied;
            _deathSource.Subscribe(_deathHandler);
        }

        /// <summary>
        /// Unsubscribes from the death source. Idempotent; after it a death changes nothing (AC-8).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _deathSource.Unsubscribe(_deathHandler);
        }

        /// <summary>Reads a copy of a player's death state (AC-9).</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="state">The state, or default when <paramref name="id"/> has no player slot.</param>
        /// <returns>True when <paramref name="id"/> holds a player slot.</returns>
        public bool TryGetDeathState(EntityID id, out DeathState state)
        {
            if (_pool.TryGetPlayerSlot(id, out byte slot))
            {
                state = _states[slot];
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>True when the player is Dead or Respawning (AC-9). False for a non-player.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <returns>Whether the player is dead.</returns>
        public bool IsDead(EntityID id)
        {
            return _pool.TryGetPlayerSlot(id, out byte slot) && _states[slot].CurrentState != DeathStateKind.Alive;
        }

        /// <summary>
        /// Test-only: forces a player's state kind (to reach Respawning before Story 004 exists). No side effects
        /// on the pool or sinks. Story 004 adds the real T-2 path; replace the Respawning test's use of this then.
        /// </summary>
        internal void SetStateForTests(EntityID id, DeathStateKind kind)
        {
            if (_pool.TryGetPlayerSlot(id, out byte slot))
            {
                _states[slot].CurrentState = kind;
            }
        }

        /// <summary>T-1 handler: Alive to Dead for a player (CR-DR-1/2/3/5, AC-1 to AC-6), ghost first.</summary>
        private void OnEntityDied(EntityID entity)
        {
            if (_disposed)
            {
                return; // a handler already queued when Dispose ran
            }

            // CR-DR-Ghost (Story 003): a ghost never enters Dead; no timer, combat ban or EntityDied. Runs before
            // CR-DR-2 and duplicate suppression. AC-6 accepted: a player who died (Dead) and then disconnected takes
            // this path on a later death until Story 007 defines that case.
            if (_ghosts.IsGhost(entity))
            {
                try
                {
                    _ghostDeaths.OnGhostDied(entity);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX}ghost death hand-off for {entity} threw: {ex}");
                }

                return;
            }

            if (!_pool.TryGetPlayerSlot(entity, out byte slot))
            {
                return; // a mob or unknown id (AC-2)
            }

            if (_states[slot].CurrentState != DeathStateKind.Alive)
            {
                return; // duplicate suppression (AC-3)
            }

            long now = _ticks.CurrentTick;
            _states[slot].CurrentState = DeathStateKind.Dead;
            _states[slot].DeathTick = now;
            _states[slot].RespawnTick = now + (long)_config.RespawnDelayTicks;

            _pool.SetSessionState(entity, ZoneSessionState.Dead);
            _combat.SetCombatProhibited(entity, true);
            _movement.SetProhibited(entity, true);
            _party.SetMemberStatus(entity, PartyMemberDeathStatus.Dead);
            // CR-DR-4: no Status Effects call; it has its own death subscription.

            EnqueueDeathMessages(entity, in _states[slot]);
        }

        private void EnqueueDeathMessages(EntityID entity, in DeathState state)
        {
            // Story 005 enqueues DeathStateEntered and EntityDied here.
        }
    }
}
