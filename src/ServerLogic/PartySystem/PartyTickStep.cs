using System;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The Party System's one phase-3 step (Party System Story 011 AC-4; ADR-014): runs, in this order,
    /// <c>PartyInviteService.Tick</c>, <c>PartyMembershipStep.ApplyQueued</c>, <c>PartyPresenceStep.Poll</c>,
    /// <c>PartyBroadcastStep.Flush</c> and <c>PartyHealthStreamStep.Run</c>. It is listed after the gateway link pump
    /// and before Enemy AI's integration step, so a kill on tick T awards XP before a leave queued on T removes the
    /// member, and the health updates leave in the same tick's outbound step.
    /// </summary>
    /// <remarks>
    /// Thin by design: no logic beyond the five calls. An exception in one sub-step is logged and the remaining
    /// sub-steps of the tick still run. Each sub-step logs only its first failure (one line naming the sub-step, with
    /// the exception); later failures of the same sub-step are silent, so a persistent fault cannot flood the log at
    /// the tick rate. The delegates are built once in the constructor, so <see cref="Run"/> allocates nothing.
    /// </remarks>
    public sealed class PartyTickStep : IZoneSimulationStep
    {
        private const string LOG_PREFIX = "[PartyTickStep] ";
        private const int SUB_STEP_COUNT = 5;

        private readonly Action<uint> _inviteTick;
        private readonly Action<uint> _applyQueued;
        private readonly Action<uint> _poll;
        private readonly Action<uint> _flush;
        private readonly Action<uint> _healthRun;
        private readonly bool[] _logged = new bool[SUB_STEP_COUNT];

        /// <summary>The invite service this step ticks; null when built with the delegate test seam.</summary>
        internal PartyInviteService Invites { get; }

        /// <summary>The membership step this step runs; null when built with the delegate test seam.</summary>
        internal PartyMembershipStep Membership { get; }

        /// <summary>The presence step this step polls; null when built with the delegate test seam.</summary>
        internal PartyPresenceStep Presence { get; }

        /// <summary>The broadcast step this step flushes; null when built with the delegate test seam.</summary>
        internal PartyBroadcastStep Broadcast { get; }

        /// <summary>The health stream step this step runs; null when built with the delegate test seam.</summary>
        internal PartyHealthStreamStep Health { get; }

        /// <summary>Creates the step over the five Party System sub-steps.</summary>
        /// <param name="invites">Invite timeouts and retries.</param>
        /// <param name="membership">Queued leave, disband and join application.</param>
        /// <param name="presence">Session-state polling (disconnect grace).</param>
        /// <param name="broadcast">The state broadcast flush.</param>
        /// <param name="health">The party health stream.</param>
        /// <exception cref="ArgumentNullException">An argument is null (the exception names it).</exception>
        public PartyTickStep(PartyInviteService invites, PartyMembershipStep membership, PartyPresenceStep presence,
            PartyBroadcastStep broadcast, PartyHealthStreamStep health)
            : this(
                Require(invites, nameof(invites)).Tick,
                Require(membership, nameof(membership)).ApplyQueued,
                Require(presence, nameof(presence)).Poll,
                FlushOf(Require(broadcast, nameof(broadcast))),
                Require(health, nameof(health)).Run,
                invites, membership, presence, broadcast, health)
        {
        }

        /// <summary>Test seam: the five sub-steps as delegates, so a test can record order and inject a throw.</summary>
        internal PartyTickStep(Action<uint> inviteTick, Action<uint> applyQueued, Action<uint> poll,
            Action<uint> flush, Action<uint> healthRun)
            : this(inviteTick, applyQueued, poll, flush, healthRun, null, null, null, null, null)
        {
        }

        private PartyTickStep(Action<uint> inviteTick, Action<uint> applyQueued, Action<uint> poll,
            Action<uint> flush, Action<uint> healthRun, PartyInviteService invites, PartyMembershipStep membership,
            PartyPresenceStep presence, PartyBroadcastStep broadcast, PartyHealthStreamStep health)
        {
            _inviteTick = inviteTick ?? throw new ArgumentNullException(nameof(inviteTick));
            _applyQueued = applyQueued ?? throw new ArgumentNullException(nameof(applyQueued));
            _poll = poll ?? throw new ArgumentNullException(nameof(poll));
            _flush = flush ?? throw new ArgumentNullException(nameof(flush));
            _healthRun = healthRun ?? throw new ArgumentNullException(nameof(healthRun));
            Invites = invites;
            Membership = membership;
            Presence = presence;
            Broadcast = broadcast;
            Health = health;
        }

        private static T Require<T>(T value, string name) where T : class
        {
            return value ?? throw new ArgumentNullException(name);
        }

        private static Action<uint> FlushOf(PartyBroadcastStep broadcast)
        {
            return tick => broadcast.Flush();
        }

        /// <inheritdoc />
        public void Run(uint currentTick, float deltaTime)
        {
            try
            {
                _inviteTick(currentTick);
            }
            catch (Exception ex)
            {
                LogFailure(0, "Tick", currentTick, ex);
            }

            try
            {
                _applyQueued(currentTick);
            }
            catch (Exception ex)
            {
                LogFailure(1, "ApplyQueued", currentTick, ex);
            }

            try
            {
                _poll(currentTick);
            }
            catch (Exception ex)
            {
                LogFailure(2, "Poll", currentTick, ex);
            }

            try
            {
                _flush(currentTick);
            }
            catch (Exception ex)
            {
                LogFailure(3, "Flush", currentTick, ex);
            }

            try
            {
                _healthRun(currentTick);
            }
            catch (Exception ex)
            {
                LogFailure(4, "HealthRun", currentTick, ex);
            }
        }

        // First failure of a sub-step logs; later failures of the same sub-step are silent.
        private void LogFailure(int index, string subStep, uint tick, Exception ex)
        {
            if (_logged[index])
            {
                return;
            }

            _logged[index] = true;
            Debug.LogError(LOG_PREFIX + subStep + " failed at tick " + tick + ": " + ex + " (logged once).");
        }
    }
}
