using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Stateless static formula for CR-OWL-2/F-OWL-1: the corrected OWL (One-Way Latency)
    /// compensation formula used to evaluate the auto-attack skill grace window. Composes with
    /// <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/> (Story 022) for the
    /// CR-OWL-1 sentinel-guarded tick-window sub-check — this class does not reimplement that
    /// sub-check or its unsigned tick-delta arithmetic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, mirroring <see cref="MobDeTargetingCoordinator"/>'s shape, not
    /// <see cref="LastBeatServerTickTracker"/>'s:</b> this class owns no per-entity or per-zone
    /// state — every call is fully described by its arguments plus the caller-supplied
    /// <see cref="LastBeatServerTickTracker"/> instance. It is CR-OWL-2's pure-math formula, not a
    /// stateful registry (that is Story 022's job, already done).
    /// </para>
    /// <para>
    /// <b>Scoping — what this class deliberately does NOT implement:</b> (1) the
    /// <c>NotifySkillUsed</c> RPC handler that will call <see cref="Evaluate"/> — a future
    /// Auto-Attack Combat epic concern, explicitly out of scope per this story's Out of Scope
    /// section; (2) the CR-NET-8.3 <c>MAX_COMPENSATABLE_OWL_MS</c> threshold gate that decides
    /// *whether* to call this formula at all — Story 024's job. <see cref="Evaluate"/> always runs
    /// the formula unconditionally when called; it has no knowledge of, and does not enforce, the
    /// OWL suspension threshold.
    /// </para>
    /// <para>
    /// <b>Unsigned arithmetic note:</b> <c>serverTickNumber</c> and <c>maxWrapWindowTicks</c> stay
    /// <see langword="uint"/> throughout and are never touched by this class directly — the
    /// sentinel-guarded tick-delta subtraction (CR-OWL-1) is entirely inside
    /// <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/>, already tested by
    /// Story 022. This class's own math (<c>signedAdjusted</c>, <c>adjustedCycleTimer</c>,
    /// <c>graceTriggers</c>) is all <see langword="float"/> — no unsigned/signed interaction here.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new LastBeatServerTickTracker();
    /// int slot = tracker.AllocatePlayerSlot(playerEntityId: 555u);
    /// tracker.RecordBeat(slot, serverTickNumber: 400u);
    ///
    /// SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
    ///     tracker, slot, serverTickNumber: 401u,
    ///     maxWrapWindowTicks: LastBeatServerTickTracker.MAX_WRAP_WINDOW_TICKS,
    ///     cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
    ///     entityId: 555u, observer);
    /// // result.WrapCorrectionActive == true, result.AdjustedCycleTimer == 0.97f, result.GraceTriggers == true
    /// </code>
    /// </example>
    public static class OwlWrapCorrectionFormula
    {
        /// <summary>
        /// Evaluates CR-OWL-2/F-OWL-1's OWL wrap-correction compensation formula for one
        /// <c>NotifySkillUsed</c> evaluation:
        /// <code>
        /// signedAdjusted = cycleTimer - owlSeconds
        /// wrapCorrectionActive = (signedAdjusted &lt; 0) AND tracker.IsWithinWrapCorrectionWindow(...)
        /// adjustedCycleTimer = wrapCorrectionActive ? cycleDuration + signedAdjusted : Math.Max(signedAdjusted, 0f)
        /// graceTriggers = adjustedCycleTimer &gt; (baseGraceThreshold * cycleDuration)
        /// </code>
        /// If <paramref name="observer"/> is supplied, fires
        /// <see cref="INetworkTestObserver.OnSkillGraceWindowEvaluated"/> with the final
        /// <c>adjustedCycleTimer</c>/<c>graceTriggers</c> values (AC-OWL-05).
        /// </summary>
        /// <param name="tracker">
        /// The zone's <see cref="LastBeatServerTickTracker"/> instance (Story 022) — supplies the
        /// CR-OWL-1 sentinel-guarded tick-window sub-check via
        /// <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="slot">The entity's slot index (CR-OWL-4), previously returned by
        /// <see cref="LastBeatServerTickTracker.AllocatePlayerSlot"/> or
        /// <see cref="LastBeatServerTickTracker.AllocateMobSlot"/>.</param>
        /// <param name="serverTickNumber">The server's current, monotonically increasing tick counter.</param>
        /// <param name="maxWrapWindowTicks">The wrap-correction window width, in ticks (GDD default
        /// <see cref="LastBeatServerTickTracker.MAX_WRAP_WINDOW_TICKS"/>).</param>
        /// <param name="cycleTimer">The entity's server-authoritative auto-attack cycle timer at the
        /// moment the <c>NotifySkillUsed</c> RPC is evaluated, in seconds.</param>
        /// <param name="owlSeconds">The server's authoritative One-Way Latency estimate for this
        /// client, in seconds (ADR-004 Decision 3: sourced from the application-level
        /// <c>RttProbe</c>, never client-submitted).</param>
        /// <param name="cycleDuration">The auto-attack cycle's total duration, in seconds.</param>
        /// <param name="baseGraceThreshold">
        /// The grace-window threshold as a fraction of <paramref name="cycleDuration"/> (GDD
        /// default 0.92). <c>graceTriggers</c> uses a strict <c>&gt;</c> comparison — a tap that
        /// lands exactly at the threshold does not trigger (per F-OWL-1's own worked example).
        /// </param>
        /// <param name="entityId">The entity ID passed through to
        /// <see cref="INetworkTestObserver.OnSkillGraceWindowEvaluated"/> when <paramref name="observer"/>
        /// is supplied.</param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <returns>
        /// A <see cref="SkillGraceWindowResult"/> carrying <c>WrapCorrectionActive</c>,
        /// <c>AdjustedCycleTimer</c>, and <c>GraceTriggers</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="tracker"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside
        /// <c>tracker</c>'s valid slot range — thrown by
        /// <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/>.</exception>
        /// <remarks>
        /// <paramref name="cycleTimer"/> and <paramref name="owlSeconds"/> are assumed non-negative,
        /// and <paramref name="cycleDuration"/> is assumed positive — these are runtime/design
        /// invariants enforced by upstream callers (the future <c>NotifySkillUsed</c> RPC handler and
        /// the GDD's Tuning Knobs table, respectively), not validated here.
        /// </remarks>
        /// <example>
        /// <code>
        /// SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
        ///     tracker, slot: 5, serverTickNumber: 401u, maxWrapWindowTicks: 2u,
        ///     cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
        ///     entityId: 555u);
        /// </code>
        /// </example>
        public static SkillGraceWindowResult Evaluate(
            LastBeatServerTickTracker tracker,
            int slot,
            uint serverTickNumber,
            uint maxWrapWindowTicks,
            float cycleTimer,
            float owlSeconds,
            float cycleDuration,
            float baseGraceThreshold,
            uint entityId
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (tracker == null)
            {
                throw new ArgumentNullException(nameof(tracker));
            }

            float signedAdjusted = cycleTimer - owlSeconds;

            bool wrapCorrectionActive = signedAdjusted < 0f
                && tracker.IsWithinWrapCorrectionWindow(slot, serverTickNumber, maxWrapWindowTicks);

            float adjustedCycleTimer = wrapCorrectionActive
                ? cycleDuration + signedAdjusted
                : Math.Max(signedAdjusted, 0f);

            bool graceTriggers = adjustedCycleTimer > (baseGraceThreshold * cycleDuration);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSkillGraceWindowEvaluated(entityId, adjustedCycleTimer, graceTriggers);
#endif

            return new SkillGraceWindowResult(wrapCorrectionActive, adjustedCycleTimer, graceTriggers);
        }
    }

    /// <summary>
    /// The three output values of <see cref="OwlWrapCorrectionFormula.Evaluate"/> (CR-OWL-2/F-OWL-1):
    /// whether the wrap-correction path activated, the final compensated cycle timer, and whether
    /// the grace window triggers.
    /// </summary>
    /// <remarks>
    /// A plain data bundle, not a behavior seam — matching <see cref="SessionHandshakeData"/>'s
    /// precedent (public readonly fields, constructor-only initialization, no
    /// <see cref="IEquatable{T}"/>/operator overloads). This result is read once per call site and
    /// never compared for equality or stored long-term.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = new SkillGraceWindowResult(wrapCorrectionActive: true, adjustedCycleTimer: 0.97f, graceTriggers: true);
    /// </code>
    /// </example>
    public readonly struct SkillGraceWindowResult
    {
        /// <summary>Whether the wrap-correction path activated (both <c>signedAdjusted &lt; 0</c> and the tracker's tick-window sub-check passed).</summary>
        public readonly bool WrapCorrectionActive;

        /// <summary>The final compensated cycle timer, in seconds, after wrap correction (or clamping to 0) has been applied.</summary>
        public readonly float AdjustedCycleTimer;

        /// <summary>Whether <c>AdjustedCycleTimer &gt; (baseGraceThreshold × cycleDuration)</c> — the grace window trigger decision.</summary>
        public readonly bool GraceTriggers;

        /// <summary>Initializes a new <see cref="SkillGraceWindowResult"/> with the specified field values.</summary>
        public SkillGraceWindowResult(bool wrapCorrectionActive, float adjustedCycleTimer, bool graceTriggers)
        {
            WrapCorrectionActive = wrapCorrectionActive;
            AdjustedCycleTimer = adjustedCycleTimer;
            GraceTriggers = graceTriggers;
        }
    }
}
