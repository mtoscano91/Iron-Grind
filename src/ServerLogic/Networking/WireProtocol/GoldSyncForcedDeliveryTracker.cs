using System.Collections.Generic;
using IronGrind.Currency;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// MCR-4's per-client, per-character consecutive-drop counter and forced-delivery anomaly
    /// tracker (Story 026). A sealed, <c>Dictionary</c>-backed stateful class — one instance per
    /// zone/connection registry, matching this folder's established per-story tracker shape
    /// (<see cref="GhostEntityTracker"/>, <see cref="LastBeatServerTickTracker"/>). Keyed by
    /// <see cref="CharacterID"/> rather than a connection/client id, matching
    /// <see cref="GoldSyncEvent"/>'s own identity space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two counters, not one — the GDD's own math only checks out this way.</b> MCR-4 describes
    /// what reads at first like a single mechanism, but it is actually two counters with two
    /// different reset rules:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b><see cref="GetConsecutiveDropCount"/></b> — increments on each tick <see cref="GoldSyncEvent"/>
    /// is R-U overflow-dropped for a character; resets to 0 on <i>any</i> confirmed delivery — a
    /// normal R-U success (<see cref="RecordRUDeliveryOutcome"/> with <c>wasDelivered: true</c>) OR a
    /// forced R-OD success (<see cref="ConfirmForcedDeliverySucceeded"/>). This is what F-MCR-1's rate
    /// formula (<c>TICK_RATE_HZ ÷ (GOLD_MAX_CONSECUTIVE_DROP + 1)</c> = 20/(3+1) = 5/s at defaults)
    /// requires: a sustained-overflow client cycles 3 drop ticks + 1 forced-delivery tick, repeating
    /// every 4 ticks, because the forced R-OD success resets <i>this</i> counter back to 0 each cycle.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b><see cref="GetTicksSinceForcedDeliveryRequired"/></b> — the anomaly-threshold counter,
    /// deliberately <i>not</i> reset by a forced R-OD success. It starts counting the tick
    /// <see cref="GetConsecutiveDropCount"/> first reaches <see cref="GOLD_MAX_CONSECUTIVE_DROP"/>
    /// (i.e. the client first enters "forced-delivery required" territory) and increments on every
    /// subsequent tick without a normal R-U delivery success — whether that tick is an ordinary drop
    /// tick or a forced-delivery-fire tick — until a normal R-U delivery actually succeeds, at which
    /// point it resets to 0. This is the only reading consistent with MCR-4's literal anomaly
    /// threshold text: "fired for more than <c>FORCED_DELIVERY_CONSECUTIVE_TICKS</c> consecutive
    /// ticks... At the default of 100 ticks <b>(5 seconds at 20Hz)</b>." That parenthetical is a
    /// 1:1 real-elapsed-tick claim. An earlier draft of this class advanced this counter only on the
    /// ticks <see cref="ConfirmForcedDeliverySucceeded"/> was called (i.e. only on fire ticks, 1 of
    /// every 4 ticks under the verified cycle above) — that reaches 100 "fire events" only after
    /// ~400 real ticks (20s), 4x slower than the GDD's own parenthetical, and was corrected before
    /// this class was ever written to disk (code review, pre-implementation).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Why the anomaly check lives in <see cref="RecordRUDeliveryOutcome"/>, not in
    /// <see cref="ConfirmForcedDeliverySucceeded"/>:</b> under the verified 4-tick sustained-overflow
    /// cycle, a fire tick's elapsed-ticks value follows <c>4k − 2</c> for the k-th fire (2, 6, 10, ...).
    /// Checking the threshold only when <see cref="ConfirmForcedDeliverySucceeded"/> is called would
    /// skip the exact tick this counter crosses 101 (since <c>4k − 2 = 101</c> has no integer solution)
    /// — the check would instead fire two ticks late, at elapsed = 102. Checking on every call to
    /// <see cref="RecordRUDeliveryOutcome"/> (the genuine once-per-tick entry point, called every tick
    /// regardless of whether that tick also happens to confirm a forced delivery) fires the anomaly on
    /// the exact tick the counter reaches <see cref="FORCED_DELIVERY_CONSECUTIVE_TICKS"/> + 1, matching
    /// "fires exactly once per anomaly streak" (the same idiom as
    /// <see cref="INetworkTestObserver.OnConnectionQualityUpdateEmitted"/>).
    /// </para>
    /// <para>
    /// <b>Never reset on disconnect or zone transition</b> (MCR-4, explicit): this class has no
    /// "clear this character's record" method at all — a reconnecting session resumes its drop count
    /// exactly where it left off, matching the GDD's "it is not forgiven" language. This is a
    /// <i>stronger</i> guarantee than <see cref="GhostEntityTracker"/>'s lifecycle: that class does
    /// have an explicit, request-driven teardown (<see cref="GhostEntityTracker.RemoveGhost"/>,
    /// Story 021) fired at ghost-cleanup — this class has no teardown method at all, by design, for
    /// any reason, ever. Its dictionary is therefore unbounded for the lifetime of whatever object
    /// holds it; a future wiring story must scope one instance per zone (constructed fresh per zone,
    /// discarded on zone teardown per ST-NET-2 <c>Draining → Closed</c>), not a server-wide singleton.
    /// </para>
    /// <para>
    /// <b>Priority-path enqueue and enhancement-exempt ordering are this class's caller's job, not
    /// this class's:</b> <see cref="ShouldForceDelivery"/> is a pure query — this class never calls
    /// <see cref="PriorityPathQueue{T}.Enqueue"/> itself. The caller is expected to enqueue the forced
    /// message with <c>isExempt: true</c> once this query returns <see langword="true"/> (the only
    /// mechanism <see cref="PriorityPathQueue{T}"/> exposes for "queue-jump, always included, never
    /// deferred" — see that class's own remarks). "Behind enhancement-exempt messages" (MCR-4's own
    /// priority-path-cap-interaction note) is therefore caller-enqueue-order-dependent, not something
    /// this class or <see cref="PriorityPathQueue{T}"/> arbitrates — the same honestly-documented
    /// limitation class as Story 018's AC-CGS-3 and Story 019's AC-GH-17. Neither of this story's two
    /// blocking ACs (AC-MCR-01, AC-MCR-07) exercises that sub-ordering.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new GoldSyncForcedDeliveryTracker();
    /// var characterId = new CharacterID(7);
    ///
    /// // Ticks T, T+1, T+2: R-U overflow-dropped three times in a row.
    /// tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // count=1
    /// tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // count=2
    /// tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // count=3 -&gt; armed
    ///
    /// // Tick T+3: caller checks the query, emits the standalone R-OD message, then confirms it.
    /// if (tracker.ShouldForceDelivery(characterId))
    /// {
    ///     // ... GoldSyncForcedDeliveryCodec.Write(...); queue.Enqueue(msg, isExempt: true); ...
    ///     tracker.ConfirmForcedDeliverySucceeded(characterId); // resets ConsecutiveDropCount only
    /// }
    /// </code>
    /// </example>
    public sealed class GoldSyncForcedDeliveryTracker
    {
        /// <summary>
        /// GDD default tuning knob (safe range [1, 5]): consecutive R-U overflow-drop ticks before
        /// forced R-OD delivery is required on the next tick (MCR-4).
        /// </summary>
        public const int GOLD_MAX_CONSECUTIVE_DROP = 3;

        /// <summary>
        /// GDD default tuning knob (safe range [60, 300]): consecutive elapsed real ticks without a
        /// normal R-U delivery success before the <c>GoldSyncForcedDelivery</c> critical anomaly logs
        /// (MCR-4). 100 ticks = 5 seconds at the fixed 20Hz tick rate.
        /// </summary>
        public const int FORCED_DELIVERY_CONSECUTIVE_TICKS = 100;

        private sealed class Record
        {
            internal int ConsecutiveDropCount;
            internal bool IsInForcedDeliveryRegime;
            internal int TicksSinceForcedDeliveryRequired;
        }

        private readonly Dictionary<uint, Record> _records = new Dictionary<uint, Record>();

        private Record GetOrCreateRecord(uint characterIdRaw)
        {
            if (!_records.TryGetValue(characterIdRaw, out Record record))
            {
                record = new Record();
                _records[characterIdRaw] = record;
            }

            return record;
        }

        /// <summary>
        /// Records this tick's normal R-U delivery outcome for <paramref name="characterId"/> — the
        /// once-per-tick entry point for both of this class's counters (see class remarks). Call this
        /// once per tick for every character with an outstanding <see cref="GoldSyncEvent"/>, before
        /// checking <see cref="ShouldForceDelivery"/> for the same tick.
        /// </summary>
        /// <param name="characterId">The character whose R-U delivery outcome this tick is being recorded.</param>
        /// <param name="wasDelivered">
        /// <see langword="true"/> if this character's <see cref="GoldSyncEvent"/> survived this tick's
        /// R-U batch (was not evicted by <see cref="RUBatchWriter"/>'s category-eviction policy);
        /// <see langword="false"/> if it was overflow-dropped this tick.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnGoldSyncForcedDeliveryAnomalyLogged</c> exactly
        /// once, on the tick <see cref="GetTicksSinceForcedDeliveryRequired"/> first exceeds
        /// <see cref="FORCED_DELIVERY_CONSECUTIVE_TICKS"/> (AC-MCR-07). This parameter only exists
        /// inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c>, matching this folder's
        /// established nullable-observer convention (<see cref="RUBatchWriter.Write"/>).
        /// </param>
        /// <example>
        /// <code>tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);</code>
        /// </example>
        public void RecordRUDeliveryOutcome(CharacterID characterId, bool wasDelivered
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            Record record = GetOrCreateRecord(characterId.RawValue);

            if (wasDelivered)
            {
                // A genuine normal R-U delivery success is the only thing that clears the
                // anomaly-threshold counter (MCR-4: "without a normal R-U delivery succeeding").
                record.ConsecutiveDropCount = 0;
                record.IsInForcedDeliveryRegime = false;
                record.TicksSinceForcedDeliveryRequired = 0;
                return;
            }

            record.ConsecutiveDropCount++;

            if (!record.IsInForcedDeliveryRegime)
            {
                if (record.ConsecutiveDropCount >= GOLD_MAX_CONSECUTIVE_DROP)
                {
                    // This tick is the one that first crosses into forced-delivery-required
                    // territory — counts as elapsed tick #1 for the anomaly counter.
                    record.IsInForcedDeliveryRegime = true;
                    record.TicksSinceForcedDeliveryRequired = 1;
                }

                return;
            }

            record.TicksSinceForcedDeliveryRequired++;

            if (record.TicksSinceForcedDeliveryRequired == FORCED_DELIVERY_CONSECUTIVE_TICKS + 1)
            {
                Debug.LogWarning($"[GoldSyncForcedDeliveryTracker] GoldSyncForcedDelivery: characterId={characterId.RawValue}, " +
                    $"consecutiveTicksWithoutNormalDelivery={record.TicksSinceForcedDeliveryRequired} — forced R-OD delivery has been " +
                    $"substituting for normal R-U delivery for more than {FORCED_DELIVERY_CONSECUTIVE_TICKS} consecutive ticks (MCR-4).");

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnGoldSyncForcedDeliveryAnomalyLogged(characterId.RawValue, record.TicksSinceForcedDeliveryRequired);
#endif
            }
        }

        /// <summary>
        /// Returns whether standalone R-OD forced delivery must fire for <paramref name="characterId"/>
        /// (MCR-4): <see langword="true"/> once <see cref="GetConsecutiveDropCount"/> has reached
        /// <see cref="GOLD_MAX_CONSECUTIVE_DROP"/>. Does not itself emit or enqueue anything — see
        /// class remarks for the caller's expected composition with
        /// <see cref="PriorityPathQueue{T}.Enqueue"/> and <see cref="GoldSyncForcedDeliveryCodec.Write"/>.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>if (tracker.ShouldForceDelivery(characterId)) { /* emit + enqueue exempt */ }</code>
        /// </example>
        public bool ShouldForceDelivery(CharacterID characterId)
            => _records.TryGetValue(characterId.RawValue, out Record record) && record.ConsecutiveDropCount >= GOLD_MAX_CONSECUTIVE_DROP;

        /// <summary>
        /// Confirms that a forced R-OD delivery succeeded for <paramref name="characterId"/> this
        /// tick (MCR-4: "resets to 0 on the first tick where <see cref="GoldSyncEvent"/> is
        /// successfully delivered via either the R-U batch or the forced R-OD path"). Resets only
        /// <see cref="GetConsecutiveDropCount"/> — deliberately does <i>not</i> clear the
        /// forced-delivery-regime/elapsed-tick anomaly state, since MCR-4's anomaly threshold is
        /// explicitly measured "without a normal R-U delivery succeeding," and a forced R-OD success
        /// is not a normal R-U delivery (see class remarks).
        /// </summary>
        /// <param name="characterId">The character whose forced delivery just succeeded.</param>
        /// <example>
        /// <code>tracker.ConfirmForcedDeliverySucceeded(characterId);</code>
        /// </example>
        public void ConfirmForcedDeliverySucceeded(CharacterID characterId)
        {
            GetOrCreateRecord(characterId.RawValue).ConsecutiveDropCount = 0;
        }

        /// <summary>
        /// Returns <paramref name="characterId"/>'s current consecutive R-U overflow-drop count,
        /// defaulting to 0 for a character with no record at all.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>int drops = tracker.GetConsecutiveDropCount(characterId);</code>
        /// </example>
        public int GetConsecutiveDropCount(CharacterID characterId)
            => _records.TryGetValue(characterId.RawValue, out Record record) ? record.ConsecutiveDropCount : 0;

        /// <summary>
        /// Returns <paramref name="characterId"/>'s current elapsed-real-ticks-since-forced-delivery-
        /// required count (MCR-4's anomaly counter — see class remarks), defaulting to 0 for a
        /// character with no record at all or one that has never entered the forced-delivery regime.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>int elapsedTicks = tracker.GetTicksSinceForcedDeliveryRequired(characterId);</code>
        /// </example>
        public int GetTicksSinceForcedDeliveryRequired(CharacterID characterId)
            => _records.TryGetValue(characterId.RawValue, out Record record) ? record.TicksSinceForcedDeliveryRequired : 0;
    }
}
