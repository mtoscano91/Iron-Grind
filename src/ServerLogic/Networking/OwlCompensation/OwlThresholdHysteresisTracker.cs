using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Networking Core Story 024: CR-NET-8.3's OWL threshold and hysteresis band — the ON/OFF
    /// signal that decides <i>whether</i> OWL compensation is active for a client session at all.
    /// A sealed, per-zone-instance stateful class tracking each client's current compensation mode
    /// by <c>entityId</c> (same per-zone-instance shape as <see cref="LastBeatServerTickTracker"/>,
    /// Story 022 — constructed fresh per zone, never a static singleton).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Does not call <see cref="OwlWrapCorrectionFormula.Evaluate"/> or touch
    /// <see cref="LastBeatServerTickTracker"/>:</b> per this story's own Out of Scope section, this
    /// class only decides the ON/OFF signal — Story 023's formula (called by an upstream caller only
    /// when this class reports the mode is <see langword="true"/>/active) and Story 022's tick
    /// tracker are separate, already-complete concerns this class does not reference.
    /// </para>
    /// <para>
    /// <b>Default mode is compensation-ACTIVE (<see langword="true"/>), not OFF:</b> CR-NET-8.3
    /// frames uncompensated mode as the exceptional/degraded state, not the default — a session
    /// that has not yet recorded any OWL sample (e.g. immediately after
    /// <c>PlayerJoinedZone</c>/session handshake, before the first <c>RttProbe</c> round-trip
    /// completes) starts in normal compensated mode.
    /// </para>
    /// <para>
    /// <b>A first sample can still emit, if it itself crosses the threshold:</b>
    /// <see cref="EvaluateOwlSample"/> computes whether the mode changed as
    /// <c>newActive != currentlyActive</c> unconditionally — it does not additionally require that
    /// <c>entityId</c> was already tracked before this call. Establishing the default baseline is
    /// silent only because it usually produces no actual change (default <see langword="true"/> in,
    /// <see langword="true"/> out, when the sample doesn't cross <see cref="MAX_COMPENSATABLE_OWL_SECONDS"/>
    /// + <see cref="OWL_HYSTERESIS_BAND_SECONDS"/>). If a session's very first-ever OWL sample
    /// already exceeds the entry threshold, that is a genuine transition away from the assumed
    /// default and the client must be told — there is no other message in this codebase that
    /// conveys initial connection-quality state at handshake, so suppressing this particular
    /// emission would leave a degraded-from-the-start session with zero information about it.
    /// </para>
    /// <para>
    /// <b>Seconds, not milliseconds:</b> <see cref="MAX_COMPENSATABLE_OWL_SECONDS"/> and
    /// <see cref="OWL_HYSTERESIS_BAND_SECONDS"/> are expressed in seconds to match
    /// <see cref="OwlWrapCorrectionFormula.Evaluate"/>'s existing <c>owlSeconds</c> parameter
    /// convention (Story 023) — this codebase works in seconds for OWL/timer values throughout;
    /// only the GDD's prose is millisecond-denominated.
    /// </para>
    /// <para>
    /// <b>Strict comparison at both boundaries, and a float-precision note:</b>
    /// <see cref="EvaluateOwlSample"/> uses <c>owlSeconds &gt; enterThreshold</c> and
    /// <c>owlSeconds &lt; exitThreshold</c> — matching CR-NET-8.3's own wording ("rises above" /
    /// "falls below") and this cluster's established strict-boundary precedent
    /// (<see cref="OwlWrapCorrectionFormula.Evaluate"/>'s <c>graceTriggers</c> strict <c>&gt;</c>).
    /// Note that <c>enterThreshold</c> is computed as <c>0.12f + 0.015f</c>, which in IEEE-754
    /// single precision equals <c>0.13499999f</c> — one bit below the literal <c>0.135f</c>
    /// (<c>0.135000005f</c>). Consequently an OWL sample of exactly the literal <c>0.135f</c> DOES
    /// cross the computed <c>enterThreshold</c> under strict <c>&gt;</c>, which matches CR-NET-8.3's
    /// documented default numbers. <c>exitThreshold</c> (<c>0.12f - 0.015f = 0.104999997f</c>)
    /// happens to be bit-identical to the literal <c>0.105f</c>, so a sample of exactly
    /// <c>0.105f</c> does NOT cross it under strict <c>&lt;</c> (it is equal, not less). This is
    /// intentional floating-point behavior of these exact tuning-knob values, not a bug — see the
    /// test file's boundary tests for the worked proof.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new OwlThresholdHysteresisTracker();
    ///
    /// // First sample -- establishes the default (true/compensated) baseline silently, since it
    /// // doesn't cross the entry threshold.
    /// bool active1 = tracker.EvaluateOwlSample(entityId: 555u, owlSeconds: 0.08f); // true, no emission
    ///
    /// // Rises above the 135ms entry threshold -- flips OFF, emits once.
    /// bool active2 = tracker.EvaluateOwlSample(entityId: 555u, owlSeconds: 0.150f, observer); // false, emits (false)
    ///
    /// // Drops below the 105ms exit threshold -- flips back ON, emits once.
    /// bool active3 = tracker.EvaluateOwlSample(entityId: 555u, owlSeconds: 0.090f, observer); // true, emits (true)
    /// </code>
    /// </example>
    public sealed class OwlThresholdHysteresisTracker
    {
        /// <summary>
        /// CR-NET-8.3's <c>MAX_COMPENSATABLE_OWL_MS</c> tuning knob, in seconds (GDD default:
        /// 120ms / 240ms RTT). Above this threshold (plus the hysteresis band — see
        /// <see cref="OWL_HYSTERESIS_BAND_SECONDS"/>), OWL compensation is not applied at all.
        /// </summary>
        public const float MAX_COMPENSATABLE_OWL_SECONDS = 0.12f;

        /// <summary>
        /// CR-NET-8.3's <c>OWL_HYSTERESIS_BAND_MS</c> tuning knob, in seconds (GDD default: 15ms).
        /// Applied symmetrically around <see cref="MAX_COMPENSATABLE_OWL_SECONDS"/> to prevent
        /// rapid <c>ConnectionQualityUpdate</c> oscillation near the threshold.
        /// </summary>
        public const float OWL_HYSTERESIS_BAND_SECONDS = 0.015f;

        private readonly Dictionary<uint, bool> _compensationActiveByEntity = new();

        /// <summary>
        /// Evaluates a new OWL sample for <paramref name="entityId"/> against CR-NET-8.3's
        /// hysteresis band, updating and returning the entity's compensation-active mode.
        /// </summary>
        /// <remarks>
        /// Logic (see class remarks for the full rationale, including the float-precision note on
        /// the exact threshold values):
        /// <list type="number">
        /// <item>Look up <paramref name="entityId"/>'s current mode, defaulting to
        /// <see langword="true"/> (compensated) if this is the first sample for this entity.</item>
        /// <item>Compute <c>enterThreshold = MAX_COMPENSATABLE_OWL_SECONDS + OWL_HYSTERESIS_BAND_SECONDS</c>
        /// (&#8776;0.135s) and <c>exitThreshold = MAX_COMPENSATABLE_OWL_SECONDS - OWL_HYSTERESIS_BAND_SECONDS</c>
        /// (&#8776;0.105s).</item>
        /// <item>If currently active and <paramref name="owlSeconds"/> &gt; <c>enterThreshold</c>,
        /// the new mode is inactive (<see langword="false"/>).</item>
        /// <item>Else if currently inactive and <paramref name="owlSeconds"/> &lt; <c>exitThreshold</c>,
        /// the new mode is active (<see langword="true"/>).</item>
        /// <item>Else, the mode is unchanged (covers both "already at the target state" and "OWL
        /// sits within the 105-135ms band").</item>
        /// <item>If (and only if) the mode actually differs from the looked-up current mode after
        /// this evaluation, fires <see cref="INetworkTestObserver.OnConnectionQualityUpdateEmitted"/>
        /// with the new mode. This check does not distinguish "entity was already tracked" from
        /// "entity just defaulted this call" — a first sample that itself crosses a threshold is a
        /// real transition and is reported (see class remarks).</item>
        /// </list>
        /// <paramref name="owlSeconds"/> is assumed non-negative — a runtime invariant enforced by
        /// the upstream <c>RttProbe</c> measurement (ADR-004 Decision 3), not validated here. This
        /// mirrors <see cref="OwlWrapCorrectionFormula.Evaluate"/>'s identical assumption about its
        /// own <c>owlSeconds</c> parameter (Story 023).
        /// </remarks>
        /// <param name="entityId">The client session's entity id.</param>
        /// <param name="owlSeconds">The server's latest authoritative OWL estimate for this client,
        /// in seconds (ADR-004 Decision 3: sourced from the application-level <c>RttProbe</c>).</param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <returns>The entity's compensation-active mode after this sample.</returns>
        /// <example>
        /// <code>
        /// bool active = tracker.EvaluateOwlSample(entityId: 555u, owlSeconds: 0.150f, observer);
        /// </code>
        /// </example>
        public bool EvaluateOwlSample(
            uint entityId,
            float owlSeconds
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            bool currentlyActive = _compensationActiveByEntity.TryGetValue(entityId, out bool existing)
                ? existing
                : true;

            const float enterThreshold = MAX_COMPENSATABLE_OWL_SECONDS + OWL_HYSTERESIS_BAND_SECONDS;
            const float exitThreshold = MAX_COMPENSATABLE_OWL_SECONDS - OWL_HYSTERESIS_BAND_SECONDS;

            bool newActive = currentlyActive;
            if (currentlyActive && owlSeconds > enterThreshold)
            {
                newActive = false;
            }
            else if (!currentlyActive && owlSeconds < exitThreshold)
            {
                newActive = true;
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            if (newActive != currentlyActive)
            {
                observer?.OnConnectionQualityUpdateEmitted(entityId, newActive);
            }
#endif

            _compensationActiveByEntity[entityId] = newActive;
            return newActive;
        }

        /// <summary>
        /// Reads back <paramref name="entityId"/>'s current compensation-active mode, if this
        /// entity has recorded at least one <see cref="EvaluateOwlSample"/> call.
        /// </summary>
        /// <param name="entityId">The client session's entity id.</param>
        /// <param name="compensationActive">The entity's current compensation-active mode, if
        /// found. Left at its default (<see langword="false"/>) if the entity has never been
        /// sampled — callers must check the return value, not assume a default from this
        /// parameter.</param>
        /// <returns><see langword="true"/> if <paramref name="entityId"/> has been sampled at
        /// least once.</returns>
        /// <example>
        /// <code>bool found = tracker.TryGetCompensationState(entityId: 555u, out bool active);</code>
        /// </example>
        public bool TryGetCompensationState(uint entityId, out bool compensationActive) =>
            _compensationActiveByEntity.TryGetValue(entityId, out compensationActive);
    }
}
