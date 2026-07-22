using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Ghost Session cluster (Stories 017-021), first story: the CR-GH-2 promotion sequence (steps
    /// 2-4: emit <c>GhostPromotionEvent</c>, freeze the entity, broadcast <c>IsGhost=true</c>) and
    /// the CR-GH-4/CR-GH-5 state constraints a ghosted character is subject to — command rejection,
    /// and damage applying identically regardless of ghost state (CGS-1/CGS-5's "no client
    /// prediction, ever" guarantee). A sealed, in-memory <c>Dictionary&lt;uint, GhostRecord&gt;</c>
    /// registry keyed by <c>characterId</c>, mirroring <see cref="ConnectionStateMachine"/>'s and
    /// <see cref="ZoneSessionStateMachine"/>'s established per-story registry shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately decoupled from <see cref="ConnectionStateMachine"/> — no dependency, same
    /// precedent as <see cref="ZoneSessionStateMachine"/>'s own class remarks:</b> this class never
    /// calls into <see cref="ConnectionStateMachine"/>. A real caller invokes
    /// <see cref="PromoteToGhost"/> alongside <see cref="ConnectionStateMachine.EvaluateTimeouts"/>'s
    /// heartbeat-timeout branch, at the same trigger point, but no orchestration layer wiring the two
    /// together exists yet — this story's own tests compose them directly, driving both classes
    /// against the same <see cref="INetworkTestObserver"/> in sequence.
    /// </para>
    /// <para>
    /// <b><see cref="PromoteToGhost"/> throws on double-promotion (judgment call, approved before
    /// implementation):</b> no blocking AC in this story requires this guard, but it matches
    /// <see cref="ConnectionStateMachine"/>'s and <see cref="ZoneSessionStateMachine"/>'s own
    /// established discipline of guarding every state-changing method's precondition rather than
    /// silently allowing a caller to re-enter an already-reached state. Because of this guard, this
    /// class deliberately does <i>not</i> add a <c>ClearGhostState</c> method — no AC in this story
    /// needs one, and every test that needs a clean slate simply constructs a fresh
    /// <see cref="GhostEntityTracker"/> instance, exactly matching
    /// <c>Session_ZoneStateMachine_Capacity_tests.cs</c>'s own per-test-instance convention. A future
    /// story (021, cleanup sequencing) is expected to add real teardown once the rest of the cluster
    /// exists.
    /// </para>
    /// <para>
    /// <b><see cref="ApplyDamage"/> never reads <c>GhostRecord.IsGhost</c> — this omission is itself
    /// the AC-CGS-5 / CGS-1 proof:</b> the method body has no branch, condition, or lookup that
    /// consults whether <paramref name="characterId"/> is currently a ghost. Damage is reduced from
    /// <c>currentHp</c> identically whether the character is ghosted or not — there is no code path
    /// in this class that could apply client-side prediction, extrapolation, or blending to a ghost's
    /// HP, because no code path here ever asks "is this a ghost?" before writing the new HP. This
    /// class also does not accept an <see cref="INetworkTestObserver"/> parameter on
    /// <see cref="ApplyDamage"/> at all — firing <c>OnServerDamageEventSerialized</c> is the caller's
    /// job (representing the real, not-yet-built Combat System's standard damage pipeline); this
    /// method is scoped to exactly one thing: "given a precomputed damage amount, reduce and track
    /// HP," mirroring <see cref="EnhancementRequestDeduplicator"/>'s explicit "generic mock proof, not
    /// real [System] logic" framing (Story 015) for a system that does not exist yet in this codebase.
    /// </para>
    /// <para>
    /// <b><see cref="GetAttackQueueDepth"/> is a trivial mock field, not a real attack queue:</b> no
    /// Combat/Skill system exists yet in this codebase, so there is nothing for this class to
    /// genuinely enqueue or dequeue. <see cref="PromoteToGhost"/> sets it to <c>0</c> (CR-GH-2 step 3:
    /// "clear queued movement/attack commands") and no method in this class ever increments it — the
    /// field exists solely so AC-GH-2's own pass-condition text ("attack-queue depth reported ... = 0
    /// for each tick") has something concrete to query across repeated ticks, standing in for the real
    /// Combat System's per-entity attack queue (forward-dependency mock, same treatment as
    /// <see cref="ApplyDamage"/>).
    /// </para>
    /// <para>
    /// <b>No <c>GHOST_COMBAT_TTL</c> timer field (explicit scope decision):</b> this story's own Out
    /// of Scope section defers the TTL countdown mechanism to Stories 019/021. None of this story's 7
    /// blocking ACs exercises a timer, and starting a timer with no expiry logic anywhere to test is
    /// not required — so this class deliberately has no tick-driven countdown state at all. A future
    /// story is expected to add it once F-GH-1's constant ambiguity (<c>GHOST_COMBAT_TTL_MINUTES</c>
    /// vs. <c>GHOST_COMBAT_TTL_MIN_S</c>) is resolved.
    /// </para>
    /// <para>
    /// <b><see cref="EncodeIsGhostField"/> is a minimal encoding-contract proof, not a wire message
    /// type:</b> CGS-2 requires <c>IsGhost</c> to be omitted entirely from the wire when
    /// <see langword="false"/> (a bandwidth optimization, not a <c>false</c> value sent). This static
    /// method proves that contract in isolation — zero bytes when <see langword="false"/>, one byte
    /// when <see langword="true"/> — without inventing a full zone-sync sub-message schema, which does
    /// not exist yet in this codebase (no zone-sync serializer story has been implemented). A future
    /// zone-sync serialization story is expected to call this and copy the returned bytes (if any)
    /// into its own sub-message body.
    /// </para>
    /// <para>
    /// <b>Story 021 addition — <see cref="RemoveGhost"/> (CR-GH-10 step 4):</b> the real teardown
    /// this class's own remarks above (see the <see cref="PromoteToGhost"/> paragraph) said "a future
    /// story (021, cleanup sequencing) is expected to add." Throws if <paramref name="characterId"/>
    /// (its parameter name in <see cref="RemoveGhost"/>) is not currently a ghost — the same
    /// defensive-guard discipline <see cref="PromoteToGhost"/>'s own double-promotion guard and
    /// <see cref="GhostXpPoolTracker.EndTracking"/>'s explicit-lifecycle guard both already establish
    /// in this cluster. Removes the character's entire record from the internal registry rather than
    /// merely flipping <see cref="GhostRecord.IsGhost"/> back to <see langword="false"/> — "the ghost
    /// entity is removed from the zone instance state" (CR-GH-10 step 4) means the entity no longer
    /// exists in this class's registry at all, not that it lingers in some third, non-ghost-but-
    /// still-tracked state this class has no other use for.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var ghostTracker = new GhostEntityTracker();
    ///
    /// // CR-GH-2 steps 2-4, fired at the same trigger point as
    /// // ConnectionStateMachine.EvaluateTimeouts' heartbeat-timeout branch:
    /// ghostTracker.PromoteToGhost(characterId: 555u, frozenPosition: (100, 0, 250), observer);
    ///
    /// // AC-GH-2: position and attack-queue depth stay frozen across every subsequent tick.
    /// bool found = ghostTracker.TryGetFrozenPosition(555u, out var position); // true, (100, 0, 250)
    /// int queueDepth = ghostTracker.GetAttackQueueDepth(555u); // 0
    ///
    /// // CR-GH-4 / AC-GH-13: a caller checks this before processing any buffered/incoming command.
    /// if (ghostTracker.ShouldRejectCommand(555u))
    /// {
    ///     // discard the buffered command without processing it
    /// }
    ///
    /// // AC-GH-3 / AC-CGS-5: damage applies identically regardless of ghost state.
    /// int newHp = ghostTracker.ApplyDamage(characterId: 555u, currentHp: 100, damageAmount: 42); // 58
    /// int trackedHp = ghostTracker.GetTrackedHp(555u); // 58, queryable on a later tick
    ///
    /// // AC-GH-19: IsGhost omitted from the wire entirely when false.
    /// byte[] omitted = GhostEntityTracker.EncodeIsGhostField(isGhost: false); // Length == 0
    /// byte[] included = GhostEntityTracker.EncodeIsGhostField(isGhost: true); // Length == 1, [0] == 0x01
    /// </code>
    /// </example>
    public sealed class GhostEntityTracker
    {
        /// <summary>
        /// One character's in-memory ghost-tracking record. A mutable reference type (not a struct),
        /// matching <see cref="ConnectionStateMachine.AccountSessionRecord"/>'s and
        /// <see cref="ZoneSessionStateMachine.ZoneSessionRecord"/>'s own reasoning: every method
        /// updates fields in place via a single dictionary lookup.
        /// </summary>
        private sealed class GhostRecord
        {
            /// <summary>Whether this character is currently ghosted.</summary>
            internal bool IsGhost;

            /// <summary>
            /// The server-side position captured at promotion time (CR-GH-2 step 3). Never updated
            /// by any method in this class while <see cref="IsGhost"/> is <see langword="true"/> —
            /// there is no "move" method that operates on a ghosted character, which is itself the
            /// AC-GH-2 freeze proof.
            /// </summary>
            internal (short x, short y, short z) FrozenPosition;

            /// <summary>
            /// Mock attack-queue depth (see class remarks) — set to <c>0</c> by
            /// <see cref="PromoteToGhost"/> and never incremented anywhere in this class.
            /// </summary>
            internal int AttackQueueDepth;

            /// <summary>
            /// The last HP value written by <see cref="ApplyDamage"/> for this character, queryable
            /// via <see cref="GetTrackedHp"/> on a later tick. Tracked identically regardless of
            /// <see cref="IsGhost"/> (see class remarks' <see cref="ApplyDamage"/> paragraph).
            /// </summary>
            internal int TrackedHp;
        }

        private readonly Dictionary<uint, GhostRecord> _records = new();

        /// <summary>
        /// Looks up <paramref name="characterId"/>'s record, creating a fresh (non-ghost, zero-HP)
        /// one if none exists yet. Used by both <see cref="PromoteToGhost"/> and
        /// <see cref="ApplyDamage"/> — the latter may target a character that has never been
        /// promoted at all, which is exactly what the AC-CGS-5 "identical regardless of ghost state"
        /// proof requires (see class remarks).
        /// </summary>
        private GhostRecord GetOrCreateRecord(uint characterId)
        {
            if (!_records.TryGetValue(characterId, out GhostRecord record))
            {
                record = new GhostRecord();
                _records[characterId] = record;
            }

            return record;
        }

        /// <summary>
        /// Executes CR-GH-2 steps 2-4 for <paramref name="characterId"/>: fires
        /// <c>OnGhostPromotionEventEmitted</c> (step 2, R-OD broadcast to all zone clients), freezes
        /// the entity at <paramref name="frozenPosition"/> and clears its tracked attack-queue depth
        /// to <c>0</c> (step 3), and sets the internal <c>IsGhost</c> flag this class exposes via
        /// <see cref="IsGhost"/> (standing in for step 4's "broadcast <c>IsGhost=true</c> on the next
        /// zone sync tick" — the actual broadcast wiring is a future zone-sync serialization story's
        /// job; this class only owns the server-side flag and freeze). Does not touch
        /// <see cref="ConnectionStateMachine"/> at all — see class remarks.
        /// </summary>
        /// <param name="characterId">The character being promoted to ghost.</param>
        /// <param name="frozenPosition">
        /// The server-side position at the moment of disconnect, in the same fixed-point centimeter
        /// encoding <see cref="IZoneTestConfigurator.GetEntityPosition"/> uses. Stored verbatim —
        /// never recomputed or mutated by this class while the character remains ghosted.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnGhostPromotionEventEmitted(characterId)</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="characterId"/> is already ghosted (see class remarks — a deliberate
        /// defensive guard, not required by any AC in this story).
        /// </exception>
        /// <example>
        /// <code>
        /// ghostTracker.PromoteToGhost(characterId: 555u, frozenPosition: (100, 0, 250), observer);
        /// </code>
        /// </example>
        public void PromoteToGhost(uint characterId, (short x, short y, short z) frozenPosition
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            GhostRecord record = GetOrCreateRecord(characterId);

            if (record.IsGhost)
            {
                throw new InvalidOperationException(
                    $"[GhostEntityTracker] {nameof(PromoteToGhost)}: characterId={characterId} is already a ghost.");
            }

            record.IsGhost = true;
            record.FrozenPosition = frozenPosition;
            record.AttackQueueDepth = 0; // CR-GH-2 step 3: clear queued movement/attack commands.

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnGhostPromotionEventEmitted(characterId);
#endif
        }

        /// <summary>
        /// Returns <paramref name="characterId"/>'s frozen position, captured at the moment
        /// <see cref="PromoteToGhost"/> was called — proves AC-GH-2's "position unchanged across 10
        /// consecutive ticks": there is no method on this class that updates
        /// <c>GhostRecord.FrozenPosition</c> while <see cref="IsGhost"/> is <see langword="true"/>, so
        /// repeated calls to this method across any number of ticks always return the same value.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <param name="position">The frozen position, if <paramref name="characterId"/> is currently ghosted.</param>
        /// <returns><see langword="true"/> if <paramref name="characterId"/> is currently ghosted.</returns>
        /// <example>
        /// <code>bool found = ghostTracker.TryGetFrozenPosition(555u, out var position);</code>
        /// </example>
        public bool TryGetFrozenPosition(uint characterId, out (short x, short y, short z) position)
        {
            if (_records.TryGetValue(characterId, out GhostRecord record) && record.IsGhost)
            {
                position = record.FrozenPosition;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// Returns whether <paramref name="characterId"/> is currently ghosted. Mirrors
        /// <see cref="ConnectionStateMachine.IsAccountRegistered"/>'s query shape.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>bool ghosted = ghostTracker.IsGhost(555u);</code>
        /// </example>
        public bool IsGhost(uint characterId)
            => _records.TryGetValue(characterId, out GhostRecord record) && record.IsGhost;

        /// <summary>
        /// CR-GH-4 / AC-GH-13: returns whether a client-originated action command (movement, attack,
        /// skill — this class has no dependency on any specific command type, since no Combat/Skill
        /// system exists yet, the same forward-dependency generic treatment as Story 010's
        /// <see cref="CrossCuttingRpcGuardChain"/>) targeting <paramref name="characterId"/> must be
        /// rejected without processing. A caller checks this before processing any buffered or
        /// newly-arrived command for the character, for the entire duration <see cref="IsGhost"/>
        /// reports <see langword="true"/> — including buffered commands sent during reconnect
        /// re-authentication (AC-GH-13), which must be discarded silently, with no position change,
        /// attack, or skill activation.
        /// </summary>
        /// <param name="characterId">The character the command targets.</param>
        /// <returns><see langword="true"/> if the command must be rejected without processing.</returns>
        /// <example>
        /// <code>
        /// if (ghostTracker.ShouldRejectCommand(555u))
        /// {
        ///     // discard the buffered command without processing it
        /// }
        /// </code>
        /// </example>
        public bool ShouldRejectCommand(uint characterId) => IsGhost(characterId);

        /// <summary>
        /// Returns the mock attack-queue depth tracked for <paramref name="characterId"/> — see class
        /// remarks for why this is a trivial mock field, not a real attack queue. Defaults to
        /// <c>0</c> for a character with no record at all.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>int depth = ghostTracker.GetAttackQueueDepth(555u); // 0</code>
        /// </example>
        public int GetAttackQueueDepth(uint characterId)
            => _records.TryGetValue(characterId, out GhostRecord record) ? record.AttackQueueDepth : 0;

        /// <summary>
        /// AC-GH-3 / AC-CGS-5 / CR-GH-5: applies a precomputed damage amount to
        /// <paramref name="characterId"/>'s tracked HP, identically regardless of whether the
        /// character is currently ghosted — see class remarks for why this method never reads
        /// <c>IsGhost</c> at all, which is itself the CGS-1 "zero client-side prediction ever applied
        /// to a ghost" structural proof. This is a minimal mock standing in for the real, not-yet-
        /// built Combat System's standard damage pipeline (see class remarks) — it does not compute
        /// damage itself, only reduces and tracks an already-computed amount.
        /// </summary>
        /// <param name="characterId">The character receiving damage.</param>
        /// <param name="currentHp">The character's HP immediately before this damage instance.</param>
        /// <param name="damageAmount">
        /// The already-computed damage amount (the real Combat System's job, not this class's) to
        /// subtract from <paramref name="currentHp"/>.
        /// </param>
        /// <returns>
        /// The new HP (<paramref name="currentHp"/> minus <paramref name="damageAmount"/>), also
        /// stored internally and queryable via <see cref="GetTrackedHp"/> on a later tick.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="damageAmount"/> is negative.</exception>
        /// <example>
        /// <code>int newHp = ghostTracker.ApplyDamage(characterId: 555u, currentHp: 100, damageAmount: 42); // 58</code>
        /// </example>
        public int ApplyDamage(uint characterId, int currentHp, int damageAmount)
        {
            if (damageAmount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damageAmount),
                    $"[GhostEntityTracker] {nameof(ApplyDamage)}: damageAmount must not be negative (was {damageAmount}).");
            }

            int newHp = currentHp - damageAmount;
            GetOrCreateRecord(characterId).TrackedHp = newHp;

            return newHp;
        }

        /// <summary>
        /// Returns the last HP value written by <see cref="ApplyDamage"/> for
        /// <paramref name="characterId"/> — the "ghost HP reported on next zone tick" query AC-GH-3
        /// and AC-CGS-5 assert against, since no <c>EntityHealthUpdate.HP</c> wire value is directly
        /// observable via <see cref="INetworkTestObserver"/> (see AC-CGS-5's resolved
        /// test-observability approach). Defaults to <c>0</c> for a character with no record at all.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>int hp = ghostTracker.GetTrackedHp(555u);</code>
        /// </example>
        public int GetTrackedHp(uint characterId)
            => _records.TryGetValue(characterId, out GhostRecord record) ? record.TrackedHp : 0;

        /// <summary>
        /// CR-GH-10 step 4 (Story 021): removes <paramref name="characterId"/>'s ghost entity from
        /// this tracker's in-memory registry entirely — "the ghost entity is removed from the zone
        /// instance state." The real teardown method this class's own remarks (see
        /// <see cref="PromoteToGhost"/>'s paragraph) deferred to a future story; this is that story.
        /// See class remarks' "Story 021 addition" paragraph for why this fully removes the record
        /// rather than merely clearing <see cref="GhostRecord.IsGhost"/>.
        /// </summary>
        /// <param name="characterId">The ghost character being removed from zone instance state.</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="characterId"/> is not currently a ghost (no record at all, or a record
        /// with <see cref="GhostRecord.IsGhost"/> already <see langword="false"/>) — matches
        /// <see cref="PromoteToGhost"/>'s own double-promotion guard precedent.
        /// </exception>
        /// <example>
        /// <code>ghostTracker.RemoveGhost(characterId: 555u);</code>
        /// </example>
        public void RemoveGhost(uint characterId)
        {
            if (!_records.TryGetValue(characterId, out GhostRecord record) || !record.IsGhost)
            {
                throw new InvalidOperationException(
                    $"[GhostEntityTracker] {nameof(RemoveGhost)}: characterId={characterId} is not currently a ghost.");
            }

            _records.Remove(characterId);
        }

        /// <summary>
        /// AC-GH-19 / CGS-2: encodes the <c>IsGhost</c> wire field's conditional-inclusion contract —
        /// omitted entirely (zero bytes) when <paramref name="isGhost"/> is <see langword="false"/>,
        /// encoded as a single <c>0x01</c> byte when <see langword="true"/>. Never sent as an explicit
        /// <c>false</c> value. This is a minimal proof of the encoding contract, not a full zone-sync
        /// wire message (see class remarks) — a future zone-sync serialization story is expected to
        /// call this and copy the returned bytes (if any) into its own sub-message body at the
        /// appropriate offset.
        /// </summary>
        /// <param name="isGhost">The entity's current ghost flag value.</param>
        /// <returns>
        /// <see cref="Array.Empty{T}"/> when <paramref name="isGhost"/> is <see langword="false"/>; a
        /// single-element array containing <c>0x01</c> when <see langword="true"/>.
        /// </returns>
        /// <example>
        /// <code>
        /// byte[] omitted = GhostEntityTracker.EncodeIsGhostField(isGhost: false); // Length == 0
        /// byte[] included = GhostEntityTracker.EncodeIsGhostField(isGhost: true); // Length == 1, [0] == 0x01
        /// </code>
        /// </example>
        public static byte[] EncodeIsGhostField(bool isGhost)
            => isGhost ? new byte[] { 1 } : Array.Empty<byte>();
    }
}
