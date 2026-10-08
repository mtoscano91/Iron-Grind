using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The fixed 20 Hz authoritative server tick loop (CR-NET-2, Story 009). Owns
    /// <see cref="ServerTickNumber"/>, the fixed simulation <see cref="FIXED_DELTA_TIME"/> every
    /// tick-driven system must use, a generic tick-driven delegate registry, a generic tick-based
    /// TTL timer registry, and EC-NET-10 tick-drift monitoring. One instance owns one server's
    /// tick cadence — this is a process-wide singleton concept, but deliberately implemented as an
    /// ordinary instantiable class (dependency-injection friendly, matching this project's
    /// "prefer dependency injection over singletons for testability" coding standard) rather than a
    /// static class or a Unity <c>MonoBehaviour</c> singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Transport-agnostic, not wired to NGO (Engine Notes / ADR-004 Decision 5):</b> this class
    /// contains pure C# tick-counting and dispatch logic only. It does not reference
    /// <c>NetworkManager.ServerTime.Tick</c>, NGO's <c>NetworkTickSystem</c>, or
    /// <c>NetworkConfig.TickRate</c> anywhere. ADR-004 Decision 5 designates
    /// <c>NetworkManager.ServerTime.Tick</c> as the eventual real clock source and requires
    /// <c>NetworkConfig.TickRate</c> to be configured to <see cref="TICK_RATE_HZ"/> (20) — both are
    /// listed as ADR-004 Verification Required items pending confirmation against
    /// <c>docs/engine-reference/unity</c>, and are explicitly out of scope for this story per its
    /// own Engine Notes. A future NGO-integration story is expected to drive <see cref="AdvanceTick"/>
    /// from NGO's real tick callback (one call per real NGO tick) instead of a test loop calling it
    /// directly, and to pass a real measured tick-processing duration as
    /// <c>actualTickDurationSeconds</c> instead of a synthetic test value. Nothing in this class's
    /// public surface needs to change for that wiring — the extension point already accepts an
    /// externally-supplied duration (see the deltaTime-vs-duration remarks below).
    /// </para>
    /// <para>
    /// <b><c>TICK_RATE_HZ</c> now exists — this is the story that introduces it:</b>
    /// <see cref="PriorityPathQueue{T}"/> (Story 006) and <see cref="RUBatchWriter"/> (Story 007)
    /// both do pure tick-count logic without assuming a running tick loop exists.
    /// <see cref="HeartbeatActivityTracker"/> (Story 008) went further and explicitly documented
    /// that <c>TICK_RATE_HZ</c> "does not exist anywhere in this codebase yet" at the time it was
    /// written — that statement is now stale (see <see cref="HeartbeatActivityTracker"/>'s updated
    /// remarks, corrected alongside this story). <see cref="TICK_RATE_HZ"/> is declared here,
    /// matching this folder's established "compile-time structural constant, not external config"
    /// precedent (<see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/>,
    /// <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/>) — because this is the first story that
    /// actually owns a running tick cadence to define it against. Wiring
    /// <see cref="HeartbeatActivityTracker"/>'s <c>intervalTicks</c> parameter to
    /// <c>HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS * TICK_RATE_HZ</c> remains a future
    /// integration story's job — not done here, per the Out of Scope section of this story.
    /// </para>
    /// <para>
    /// <b>Resolving the Story 006/007 "Flush methods" Dependencies-text imprecision:</b> this
    /// story's own Dependencies section states "Depends on: Story 006, Story 007 (tick loop calls
    /// their Flush methods)" — this is only half-accurate, and is corrected here the same way
    /// <see cref="PriorityPathQueue{T}"/> and <see cref="RUBatchWriter"/> each documented their own
    /// judgment calls on ambiguous story text:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b><see cref="PriorityPathQueue{T}"/> genuinely has a stateful <c>Flush(uint tickNumber)</c>
    /// method.</b> A future story that owns a real per-connection <see cref="PriorityPathQueue{T}"/>
    /// instance can register <c>tickLoop.RegisterTickDriven(dt =&gt; queue.Flush(tickLoop.ServerTickNumber))</c>
    /// (or an equivalent named-method delegate — see the AOT lambda-capture guard, Story 008) as one
    /// tick-driven delegate per connection. This story does not itself construct or register any
    /// <see cref="PriorityPathQueue{T}"/> instance, because no connection/session registry exists
    /// yet to own one — see Risks/Follow-ups.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b><see cref="RUBatchWriter"/> (and its siblings <c>CycleBroadcastPacketWriter</c>,
    /// <c>PositionPacketWriter</c>) has no <c>Flush()</c> method and no internal queue to flush —
    /// it is a pure stateless static function</b> taking caller-supplied <c>IReadOnlyList&lt;T&gt;</c>
    /// content per call. Story 007 deliberately modeled per-tick batch content as caller-supplied,
    /// not an accumulator; there is currently no code anywhere in this codebase that accumulates
    /// "this tick's pending <c>DamageEvent</c>s for client X" between ticks — that accumulator has
    /// not been designed yet by any story. Inventing one here would be scope creep and pure
    /// guesswork about a system this story's brief explicitly says not to build.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Resolution adopted:</b> both cases are satisfied by the exact same generic extension
    /// point — <see cref="RegisterTickDriven"/>. A future story that builds the R-U accumulator can
    /// register <c>tickLoop.RegisterTickDriven(dt =&gt; RUBatchWriter.Write(...))</c> the same way a
    /// future story wires <see cref="PriorityPathQueue{T}.Flush"/>. This story builds only the
    /// generic dispatch mechanism (<see cref="RegisterTickDriven"/>) — it does not construct, own,
    /// or fake either system's real content.
    /// </para>
    /// <para>
    /// <b>The single most load-bearing invariant in this class (CR-NET-2):</b>
    /// <see cref="FIXED_DELTA_TIME"/> — never a measured wall-clock delta — is what every
    /// tick-driven delegate receives as its <c>deltaTime</c> argument, every tick, even under tick
    /// drift (EC-NET-10). This resolves Auto-Attack GDD OQ-1. <b>Do not conflate this with
    /// <c>actualTickDurationSeconds</c></b>, the separate, optional, monitoring-only value passed to
    /// <see cref="AdvanceTick"/>: <c>actualTickDurationSeconds</c> feeds only
    /// <see cref="RecordDriftSample"/>'s EC-NET-10 performance-alert bookkeeping and never reaches
    /// any tick-driven delegate or influences <see cref="ServerTickNumber"/>'s advancement. Keeping
    /// these two values distinct in both naming and code path is the entire point of EC-NET-10 —
    /// conflating them would silently reintroduce the exact bug CR-NET-2 forbids.
    /// </para>
    /// <para>
    /// <b>Tick drift monitoring (EC-NET-10), a judgment call on window shape:</b> the GDD specifies
    /// "average over a 60-second window" without mandating a continuously-sliding vs. a periodic
    /// (reset-per-window) rolling average. This implementation adopts a periodic, non-overlapping
    /// window of exactly <see cref="DRIFT_WINDOW_TICKS"/> (= 60 seconds × <see cref="TICK_RATE_HZ"/>
    /// = 1200 ticks) samples: it accumulates a running sum and count, and — once
    /// <see cref="DRIFT_WINDOW_TICKS"/> samples have been collected — computes the window's average
    /// drift, logs a <see cref="Debug.LogWarning"/> performance alert if it exceeds
    /// <see cref="DRIFT_ALERT_THRESHOLD_SECONDS"/> (25 ms), and resets the accumulator for the next
    /// window. This is deliberately simpler and zero-allocation compared to a true continuously-
    /// sliding window (which would require a circular sample buffer), and is judged a faithful
    /// reading of "average... over a... window" — a periodic window average, evaluated once per
    /// window boundary. This is monitoring only: it never alters <see cref="ServerTickNumber"/>
    /// advancement or any tick-driven delegate's behavior, matching EC-NET-10's explicit "not
    /// handled gracefully at runtime — this is a monitoring signal, not a correction mechanism."
    /// </para>
    /// <para>
    /// <b>TTL timer registry, wraparound-safe (matches this folder's established convention):</b>
    /// <see cref="RegisterTtlTimer"/> stores an expiry tick and a callback; expiry is evaluated each
    /// <see cref="AdvanceTick"/> call via <see cref="StaleDiscardComparer.IsTickExpired"/> (Story
    /// 005) — never <see cref="System.DateTime.UtcNow"/>, per this folder's stale-discard/TTL
    /// convention. A timer registered with an expiry tick already at-or-before the current
    /// <see cref="ServerTickNumber"/> is not silently dropped: it fires on the very next
    /// <see cref="AdvanceTick"/> call, because <see cref="StaleDiscardComparer.IsTickExpired"/>'s
    /// equality-inclusive semantics mean any current-or-later tick reports it as expired.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tickLoop = new ServerTickLoop();
    ///
    /// // A future system hooks a generic per-tick delegate (e.g. a Beat-cadence advance):
    /// tickLoop.RegisterTickDriven(deltaTime =&gt; myCombatSystem.AdvanceAllCycleTimers(deltaTime));
    ///
    /// // A future system registers a generic TTL timer (e.g. a respec-scroll or ghost-combat TTL):
    /// tickLoop.RegisterTtlTimer(expiryTick: tickLoop.ServerTickNumber + 100u, onExpired: () =&gt; ReleaseResource());
    ///
    /// // Driven once per real server tick (a future NGO-integration story wires this to the real callback):
    /// tickLoop.AdvanceTick();
    /// </code>
    /// </example>
    public sealed class ServerTickLoop
    {
        /// <summary>
        /// The fixed server tick rate in Hz (CR-NET-2). ADR-004 Decision 5 requires NGO's
        /// <c>NetworkConfig.TickRate</c> to eventually be configured to this same value — that
        /// wiring is out of scope for this story (Verification Required against
        /// <c>docs/engine-reference/unity</c>). A compile-time structural constant, not read from
        /// external config, matching this folder's other structural constants.
        /// </summary>
        public const int TICK_RATE_HZ = 20;

        /// <summary>
        /// The fixed simulation delta time, in seconds, every tick-driven delegate receives every
        /// tick (CR-NET-2): <c>1.0f / <see cref="TICK_RATE_HZ"/></c> = 0.05s. Never a measured
        /// wall-clock delta, even under tick drift (EC-NET-10) — see class remarks.
        /// </summary>
        public const float FIXED_DELTA_TIME = 1.0f / TICK_RATE_HZ;

        /// <summary>
        /// EC-NET-10's monitoring window width, expressed in ticks: 60 seconds at
        /// <see cref="TICK_RATE_HZ"/> = 1200 ticks. See class remarks for the periodic-window
        /// judgment call this constant governs.
        /// </summary>
        public const int DRIFT_WINDOW_TICKS = 60 * TICK_RATE_HZ;

        /// <summary>
        /// EC-NET-10's performance-alert threshold: an average per-tick drift (actual tick
        /// duration minus <see cref="FIXED_DELTA_TIME"/>) exceeding 25 ms over a
        /// <see cref="DRIFT_WINDOW_TICKS"/>-tick window logs a performance alert.
        /// </summary>
        public const float DRIFT_ALERT_THRESHOLD_SECONDS = 0.025f;

        private readonly List<Action<float>> _tickDrivenDelegates = new List<Action<float>>();
        private readonly List<TtlTimerEntry> _ttlTimers = new List<TtlTimerEntry>();

        // Reentrancy guard for the tick-driven dispatch loop (bug fix, Story 009 code review): see
        // UnregisterTickDriven and AdvanceTick remarks below for why removals are deferred while
        // _isDispatchingTickDriven is true.
        private bool _isDispatchingTickDriven;
        private readonly List<Action<float>> _pendingTickDrivenUnregistrations = new List<Action<float>>();

        private float _driftWindowSumSeconds;
        private int _driftWindowSampleCount;

        /// <summary>
        /// The current server tick count, incremented by exactly 1 on every
        /// <see cref="AdvanceTick"/> call — never more, never based on measured elapsed time
        /// (EC-NET-10, AC-TICK-1). Wraps from <see cref="uint.MaxValue"/> back to 0 via ordinary
        /// unchecked <c>uint</c> arithmetic; all comparisons against this value elsewhere in this
        /// codebase must route through <see cref="StaleDiscardComparer"/> to remain wraparound-safe.
        /// </summary>
        public uint ServerTickNumber { get; private set; }

        /// <summary>
        /// Constructs a new tick loop. <paramref name="initialTickNumber"/> defaults to 0 for a
        /// freshly-started server, but is exposed to support future session-resume scenarios (a
        /// server restoring <see cref="ServerTickNumber"/> from persisted state) and to let tests
        /// deterministically exercise <see cref="ServerTickNumber"/>'s <c>uint</c> wraparound
        /// behavior near <see cref="uint.MaxValue"/> without advancing billions of real ticks.
        /// </summary>
        /// <param name="initialTickNumber">The starting value of <see cref="ServerTickNumber"/>.</param>
        /// <example>
        /// <code>
        /// var freshLoop = new ServerTickLoop(); // ServerTickNumber starts at 0
        /// var resumedLoop = new ServerTickLoop(initialTickNumber: 500_000u); // e.g. restored from persisted state
        /// </code>
        /// </example>
        public ServerTickLoop(uint initialTickNumber = 0u)
        {
            ServerTickNumber = initialTickNumber;
        }

        /// <summary>
        /// Registers a generic tick-driven delegate, invoked once per <see cref="AdvanceTick"/>
        /// call with <see cref="FIXED_DELTA_TIME"/> — never a measured wall-clock delta. This is
        /// the single generic extension point downstream systems (Auto-Attack Combat cycle timers,
        /// cooldown/duration counters, a future <see cref="PriorityPathQueue{T}.Flush"/> wiring, a
        /// future R-U batch accumulator's <see cref="RUBatchWriter.Write"/> wiring) hook into
        /// without this story needing to know about their specific domain logic (per the story's
        /// own Implementation Notes). Multiple delegates may be registered; each fires independently
        /// every tick, in registration order, and an exception thrown by one does not prevent
        /// registration or invocation of the others across separate calls to
        /// <see cref="AdvanceTick"/> (though within a single <see cref="AdvanceTick"/> call, an
        /// unhandled exception from one delegate propagates out of that <see cref="AdvanceTick"/>
        /// call entirely — skipping any delegates registered after it, that tick's TTL expiry pass,
        /// its EC-NET-10 drift sample, and its test/dev-build observer's <c>OnTickCompleted</c>
        /// callback — before being rethrown to the caller. <see cref="ServerTickNumber"/> has
        /// already been incremented by that point and is not rolled back; no internal state is
        /// corrupted, and skipped TTL timers are simply evaluated on a later <see cref="AdvanceTick"/>
        /// call. Callers registering delegates with side effects that must always run should guard
        /// their own callback bodies). A delegate registered <i>during</i> dispatch (from within
        /// another tick-driven delegate) is not invoked until the next <see cref="AdvanceTick"/>
        /// call — the set of delegates dispatched for a given call is snapshotted at the start of
        /// that call's dispatch pass.
        /// </summary>
        /// <param name="callback">
        /// The delegate to invoke each tick, receiving <see cref="FIXED_DELTA_TIME"/> as its sole
        /// argument. Must not be <see langword="null"/>.
        /// </param>
        /// <example>
        /// <code>
        /// tickLoop.RegisterTickDriven(deltaTime =&gt; warrior.AdvanceCycleTimer(deltaTime));
        /// </code>
        /// </example>
        public void RegisterTickDriven(Action<float> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            _tickDrivenDelegates.Add(callback);
        }

        /// <summary>
        /// Removes a previously-registered tick-driven delegate so it no longer fires on subsequent
        /// <see cref="AdvanceTick"/> calls (e.g. when the owning connection/entity is destroyed).
        /// Matches delegate identity via <see cref="Delegate.Equals(object)"/> — a lambda passed
        /// directly to <see cref="RegisterTickDriven"/> cannot be unregistered unless the same
        /// delegate instance is retained by the caller (this also mirrors why this folder's AOT
        /// guard, Story 008, discourages unnamed-lambda handler registration).
        /// </summary>
        /// <remarks>
        /// <b>Reentrancy (bug fix, Story 009 code review):</b> if called from within a tick-driven
        /// delegate — i.e. while <see cref="AdvanceTick"/>'s dispatch pass is in progress — the
        /// actual removal is deferred until that dispatch pass completes, rather than mutating
        /// <see cref="_tickDrivenDelegates"/> live. The original implementation removed immediately
        /// in all cases, which let a delegate unregistering an earlier-registered sibling shift the
        /// list out from under the in-progress loop and silently skip a not-yet-invoked delegate for
        /// that same tick (the same hazard <see cref="RegisterTtlTimer"/>'s backward-iteration TTL
        /// loop was already written to avoid). This method's return value is unaffected by the
        /// deferral: it still reports <see langword="true"/> immediately if a matching registration
        /// existed, even though the list mutation itself happens slightly later.
        /// </remarks>
        /// <param name="callback">The exact delegate instance previously passed to <see cref="RegisterTickDriven"/>.</param>
        /// <returns><see langword="true"/> if a matching registration was found (and removed, or queued for removal if called during dispatch).</returns>
        /// <example>
        /// <code>
        /// Action&lt;float&gt; handler = deltaTime =&gt; warrior.AdvanceCycleTimer(deltaTime);
        /// tickLoop.RegisterTickDriven(handler);
        /// // ... later, e.g. on despawn ...
        /// tickLoop.UnregisterTickDriven(handler);
        /// </code>
        /// </example>
        public bool UnregisterTickDriven(Action<float> callback)
        {
            if (_isDispatchingTickDriven)
            {
                if (!_tickDrivenDelegates.Contains(callback))
                {
                    return false;
                }

                if (!_pendingTickDrivenUnregistrations.Contains(callback))
                {
                    _pendingTickDrivenUnregistrations.Add(callback);
                }

                return true;
            }

            return _tickDrivenDelegates.Remove(callback);
        }

        /// <summary>
        /// Registers a generic tick-based TTL timer (AC-TICK-2). <paramref name="onExpired"/> fires
        /// once, on the first <see cref="AdvanceTick"/> call whose resulting
        /// <see cref="ServerTickNumber"/> is at-or-past <paramref name="expiryTick"/> (wraparound-safe
        /// via <see cref="StaleDiscardComparer.IsTickExpired"/>), then the registration is removed —
        /// this is a one-shot timer, not a recurring one. This is the generic extension point a
        /// future story wires a real TTL business rule (e.g. a respec-scroll TTL, once the
        /// Inventory System GDD exists — explicitly out of scope here) through; this story proves
        /// only the generic mechanism, via a mock timer.
        /// </summary>
        /// <param name="expiryTick">
        /// The tick at which this timer expires. If already at-or-before the current
        /// <see cref="ServerTickNumber"/> at registration time, <paramref name="onExpired"/> fires
        /// on the very next <see cref="AdvanceTick"/> call — it is never silently dropped.
        /// </param>
        /// <param name="onExpired">The callback to invoke exactly once, at expiry. Must not be <see langword="null"/>.</param>
        /// <example>
        /// <code>
        /// tickLoop.RegisterTtlTimer(expiryTick: tickLoop.ServerTickNumber + 100u, onExpired: () =&gt; ReleaseGhostCombat(entityId));
        /// </code>
        /// </example>
        public void RegisterTtlTimer(uint expiryTick, Action onExpired)
        {
            if (onExpired == null)
            {
                throw new ArgumentNullException(nameof(onExpired));
            }

            _ttlTimers.Add(new TtlTimerEntry(expiryTick, onExpired));
        }

        /// <summary>
        /// Advances the server by exactly one tick (CR-NET-2, AC-TICK-1): increments
        /// <see cref="ServerTickNumber"/> by 1, invokes every registered tick-driven delegate with
        /// <see cref="FIXED_DELTA_TIME"/>, fires and removes any TTL timer whose expiry has been
        /// reached (AC-TICK-2), records <paramref name="actualTickDurationSeconds"/> into the
        /// EC-NET-10 drift-monitoring window, and finally invokes the test/dev-build observer's
        /// <c>OnTickCompleted</c> hook if <paramref name="observer"/> is
        /// non-null (AC-NC-04, AC-NC-05, AC-TICK-2). <see cref="ServerTickNumber"/> never advances
        /// by more than 1 per call, regardless of how long a tick actually took — this loop never
        /// runs multiple ticks to compensate for a slow tick (EC-NET-10).
        /// </summary>
        /// <remarks>
        /// Not reentrant: no tick-driven delegate or TTL callback registered on this instance should
        /// call <see cref="AdvanceTick"/> again on the same instance from within its own dispatch
        /// pass — doing so would reset the dispatch reentrancy guard the outer call is still relying
        /// on (see <see cref="UnregisterTickDriven"/> remarks). No code in this codebase does this;
        /// a future NGO-integration story drives this from a single external tick source instead.
        /// </remarks>
        /// <param name="actualTickDurationSeconds">
        /// The measured wall-clock duration this tick's processing actually took, in seconds. Used
        /// <b>only</b> for EC-NET-10 drift monitoring — never passed to any tick-driven delegate and
        /// never used for <see cref="ServerTickNumber"/> advancement (see class remarks on why this
        /// is a fundamentally different concept from <see cref="FIXED_DELTA_TIME"/>). Defaults to
        /// <see cref="FIXED_DELTA_TIME"/> (i.e. "no drift") for callers that do not care about drift
        /// monitoring, such as most deterministic tests.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. When non-null, its <c>OnTickCompleted</c> hook fires
        /// once, after all tick-driven processing and TTL expiry checks for this tick complete.
        /// Matches this folder's established nullable-observer convention (<see cref="RUBatchWriter.Write"/>).
        /// This parameter only exists inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> —
        /// see remarks on why the observer's interface type is never referenced in an unguarded
        /// production context (Story 002 release-stripping contract, AC-TC-02; this was a real
        /// defect here until Story 010's code review surfaced and fixed it project-wide).
        /// </param>
        /// <example>
        /// <code>
        /// tickLoop.AdvanceTick(); // typical production/test call — no drift tracked, no observer
        /// tickLoop.AdvanceTick(actualTickDurationSeconds: 0.08f, observer: myTestObserver); // a slow tick, observed
        /// </code>
        /// </example>
        public void AdvanceTick(float actualTickDurationSeconds = FIXED_DELTA_TIME
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            ServerTickNumber++;

            // Tick-driven dispatch (bug fix, Story 009 code review): the delegate count is
            // snapshotted before dispatch so a delegate registered mid-dispatch is deferred to the
            // next AdvanceTick call, and _isDispatchingTickDriven defers any UnregisterTickDriven
            // calls made from within a delegate until after this pass completes (see
            // UnregisterTickDriven remarks) — both guard against the list shifting underneath this
            // loop and silently skipping a not-yet-invoked delegate.
            _isDispatchingTickDriven = true;
            try
            {
                int tickDrivenCount = _tickDrivenDelegates.Count;
                for (int i = 0; i < tickDrivenCount; i++)
                {
                    _tickDrivenDelegates[i](FIXED_DELTA_TIME);
                }
            }
            finally
            {
                _isDispatchingTickDriven = false;

                if (_pendingTickDrivenUnregistrations.Count > 0)
                {
                    for (int i = 0; i < _pendingTickDrivenUnregistrations.Count; i++)
                    {
                        _tickDrivenDelegates.Remove(_pendingTickDrivenUnregistrations[i]);
                    }

                    _pendingTickDrivenUnregistrations.Clear();
                }
            }

            // TTL expiry check (AC-TICK-2). Iterated backward so RemoveAt does not disturb the
            // index of any not-yet-visited entry; callbacks that register new TTL timers during
            // this loop are safe (new entries land past the original bound and are evaluated on a
            // future AdvanceTick call, not this one). No ordering guarantee is made among multiple
            // timers that expire on the same tick.
            for (int i = _ttlTimers.Count - 1; i >= 0; i--)
            {
                TtlTimerEntry entry = _ttlTimers[i];
                if (StaleDiscardComparer.IsTickExpired(ServerTickNumber, entry.ExpiryTick))
                {
                    _ttlTimers.RemoveAt(i);
                    entry.OnExpired();
                }
            }

            RecordDriftSample(actualTickDurationSeconds);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnTickCompleted(ServerTickNumber);
#endif
        }

        /// <summary>
        /// Accumulates one EC-NET-10 drift sample into the current periodic monitoring window (see
        /// class remarks for the periodic-vs-sliding-window judgment call), logging a
        /// <see cref="Debug.LogWarning"/> performance alert and resetting the window once
        /// <see cref="DRIFT_WINDOW_TICKS"/> samples have accumulated, if the window's average drift
        /// exceeds <see cref="DRIFT_ALERT_THRESHOLD_SECONDS"/>. Monitoring only — never influences
        /// <see cref="ServerTickNumber"/> or any tick-driven delegate.
        /// </summary>
        private void RecordDriftSample(float actualTickDurationSeconds)
        {
            float driftSeconds = actualTickDurationSeconds - FIXED_DELTA_TIME;
            _driftWindowSumSeconds += driftSeconds;
            _driftWindowSampleCount++;

            if (_driftWindowSampleCount < DRIFT_WINDOW_TICKS)
            {
                return;
            }

            float averageDriftSeconds = _driftWindowSumSeconds / _driftWindowSampleCount;
            if (averageDriftSeconds > DRIFT_ALERT_THRESHOLD_SECONDS)
            {
                Debug.LogWarning($"[ServerTickLoop] TickDriftAlert: average tick drift over the last " +
                    $"{_driftWindowSampleCount} ticks ({DRIFT_WINDOW_TICKS}-tick / 60s-equivalent window per EC-NET-10) " +
                    $"is {averageDriftSeconds * 1000f:F2}ms, exceeding the {DRIFT_ALERT_THRESHOLD_SECONDS * 1000f:F2}ms " +
                    "alert threshold. This is a monitoring signal only — ServerTickNumber advance is never affected by tick drift.");
            }

            _driftWindowSumSeconds = 0f;
            _driftWindowSampleCount = 0;
        }

        /// <summary>
        /// A single registered TTL timer (AC-TICK-2): an expiry tick and its one-shot callback.
        /// Private, immutable value type — no heap allocation beyond the enclosing <see cref="List{T}"/> storage.
        /// </summary>
        private readonly struct TtlTimerEntry
        {
            internal readonly uint ExpiryTick;
            internal readonly Action OnExpired;

            internal TtlTimerEntry(uint expiryTick, Action onExpired)
            {
                ExpiryTick = expiryTick;
                OnExpired = onExpired;
            }
        }
    }
}
