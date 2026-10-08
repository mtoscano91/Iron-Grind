using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Ghost Session cluster (Stories 017-021), fourth story: the CR-GH-8.1 two-pool XP bookkeeping
    /// underlying the CR-GH-9/CR-GH-9.1/CR-GH-9.2 forfeit-vs-restore policy. A sealed, in-memory
    /// <c>Dictionary&lt;uint, XpPoolRecord&gt;</c> registry keyed by <c>characterId</c>, mirroring
    /// <see cref="GhostEntityTracker"/>'s own per-story registry shape (Story 017).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two pools, one method resolving both (CR-GH-8.1):</b> the pre-disconnect pool is banked
    /// once at <see cref="BeginTracking"/> and never mutated afterward by any method on this class --
    /// there is no "add to pre-disconnect pool" method anywhere in this file, which is itself the
    /// CR-GH-8.1 "never forfeited" structural proof. <see cref="AccumulatePostDisconnectShare"/> grows
    /// the only pool <see cref="ResolveFinalXp"/>'s <c>includePostDisconnectShare</c> parameter can
    /// ever discard.
    /// </para>
    /// <para>
    /// <b>Explicit-lifecycle lookups, unlike <see cref="GhostEntityTracker"/>'s default-to-zero query
    /// style (deliberate divergence, approved before implementation):</b> every query and mutation
    /// method below throws <see cref="InvalidOperationException"/> for an untracked
    /// <c>characterId</c>, rather than silently returning 0. <see cref="GhostEntityTracker"/> can
    /// safely default-to-zero because its queries (<c>GetTrackedHp</c>, <c>GetAttackQueueDepth</c>)
    /// model values that are meaningfully zero for a character that was never ghosted. A silently
    /// returned 0 XP here could instead mask a real bug -- a caller resolving final XP for a
    /// character it forgot to call <see cref="BeginTracking"/> for would otherwise persist 0 XP
    /// without ever knowing something was wrong. This class trades convenience for that safety.
    /// </para>
    /// <para>
    /// <b><see cref="ResolveFinalXp"/> is the single CR-GH-9/9.1/9.2 decision point:</b> every
    /// forfeit-vs-restore outcome across this story's 5 blocking ACs reduces to calling this one
    /// method with a different <c>includePostDisconnectShare</c> value -- <see langword="false"/> for
    /// TTL expiry (AC-GH-7) and ghost death (AC-GH-12), the same forfeit rule per CR-GH-9.1;
    /// <see langword="true"/> only for a successful reconnect (AC-GH-8). No other branching exists
    /// anywhere in this class or in <see cref="PartyDisbandCoordinator"/> for this decision.
    /// </para>
    /// <para>
    /// <b><see cref="AccumulatePostDisconnectShare"/> no-ops once <see cref="StopAccumulation"/> has
    /// been called -- this IS the AC-GH-18 structural proof</b>, not merely a defensive guard: EC-GH-7
    /// requires that once a party disbands mid-ghost-period, no further XP is ever added to the
    /// post-disconnect pool. Rather than requiring every future caller site (party-award code that
    /// does not exist yet) to separately check "has this party disbanded," the guarantee is enforced
    /// once, here, unconditionally. <see cref="PartyDisbandCoordinator.ProcessPartyDisband"/> is the
    /// only caller of <see cref="StopAccumulation"/> in this codebase.
    /// </para>
    /// <para>
    /// <b>No dependency on <see cref="ConnectionStateMachine"/> or <see cref="GhostEntityTracker"/>:</b>
    /// this class tracks only XP pools, keyed by the same <c>characterId</c> those classes use, but
    /// never calls into either -- matching <see cref="GhostEntityTracker"/>'s own established
    /// decoupling precedent (see that class's remarks). A real caller drives all three classes
    /// side-by-side against the same tick/event stream; this story's own tests compose them directly.
    /// <b>Sync risk for a future real caller (code review finding, Story 020):</b> because this
    /// registry and <see cref="GhostEntityTracker"/> are independently keyed by <c>characterId</c> with
    /// nothing keeping them in sync, a real production call site must call <see cref="BeginTracking"/>
    /// and <see cref="GhostEntityTracker.PromoteToGhost"/> from the same trigger point (the CGS-3
    /// snapshot moment) -- if a future caller invokes only one of the two, a legitimately-ghosted
    /// character will be untracked here and every method on this class will throw for it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var xpTracker = new GhostXpPoolTracker();
    ///
    /// // At the Connected -&gt; Disconnected_SessionActive transition (CGS-3 snapshot moment):
    /// xpTracker.BeginTracking(characterId: 555u, preDisconnectXp: 500);
    ///
    /// // Party XP shares awarded while IsGhost=true (CR-GH-8.1):
    /// xpTracker.AccumulatePostDisconnectShare(characterId: 555u, xpAmount: 40);
    /// xpTracker.AccumulatePostDisconnectShare(characterId: 555u, xpAmount: 60); // running total: 100
    ///
    /// // AC-GH-7 / AC-GH-12 -- TTL expiry or ghost death: forfeit the post-disconnect pool.
    /// int forfeitedResult = xpTracker.ResolveFinalXp(555u, includePostDisconnectShare: false); // 500
    ///
    /// // AC-GH-8 -- successful reconnect: restore the post-disconnect pool.
    /// int restoredResult = xpTracker.ResolveFinalXp(555u, includePostDisconnectShare: true); // 600
    ///
    /// xpTracker.EndTracking(555u);
    /// </code>
    /// </example>
    public sealed class GhostXpPoolTracker
    {
        /// <summary>
        /// One character's in-memory XP-pool record. A mutable reference type (not a struct),
        /// matching <see cref="GhostEntityTracker"/>'s own private record's reasoning: every method
        /// updates fields in place via a single dictionary lookup.
        /// </summary>
        private sealed class XpPoolRecord
        {
            /// <summary>
            /// The XP total banked at <see cref="BeginTracking"/> (CGS-3 snapshot moment). Never
            /// mutated afterward by any method in this class (CR-GH-8.1 "never forfeited").
            /// </summary>
            internal int PreDisconnectXp;

            /// <summary>
            /// The accumulating, forfeitable post-disconnect party XP share pool (CR-GH-8.1). Grown
            /// only by <see cref="AccumulatePostDisconnectShare"/>, and only while
            /// <see cref="AccumulationStopped"/> is <see langword="false"/>.
            /// </summary>
            internal int PostDisconnectShareXp;

            /// <summary>
            /// Set by <see cref="StopAccumulation"/> (AC-GH-18 / EC-GH-7). Once
            /// <see langword="true"/>, <see cref="AccumulatePostDisconnectShare"/> no-ops for this
            /// character for the remainder of its tracked lifetime.
            /// </summary>
            internal bool AccumulationStopped;
        }

        private readonly Dictionary<uint, XpPoolRecord> _records = new();

        /// <summary>
        /// Looks up <paramref name="characterId"/>'s record, throwing
        /// <see cref="InvalidOperationException"/> if it is not currently tracked -- see class
        /// remarks for why this class enforces explicit lifecycle rather than
        /// <see cref="GhostEntityTracker"/>'s default-to-zero query style.
        /// </summary>
        private XpPoolRecord RequireRecord(uint characterId, string callerName)
        {
            if (!_records.TryGetValue(characterId, out XpPoolRecord record))
            {
                throw new InvalidOperationException(
                    $"[GhostXpPoolTracker] {callerName}: characterId={characterId} is not currently tracked. " +
                    $"Call {nameof(BeginTracking)} first.");
            }

            return record;
        }

        /// <summary>
        /// Begins XP-pool tracking for <paramref name="characterId"/> (CGS-3 snapshot moment: the
        /// <c>Connected -&gt; Disconnected_SessionActive</c> transition), banking
        /// <paramref name="preDisconnectXp"/> as the pre-disconnect pool (CR-GH-8.1) and starting the
        /// post-disconnect pool at 0.
        /// </summary>
        /// <param name="characterId">The character beginning ghost-period XP tracking.</param>
        /// <param name="preDisconnectXp">
        /// The character's total XP at the moment of disconnect. Banked verbatim, queryable via
        /// <see cref="GetPreDisconnectXp"/> -- never forfeited regardless of ghost-period outcome
        /// (CR-GH-8.1).
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="characterId"/> is already tracked -- matches
        /// <see cref="GhostEntityTracker.PromoteToGhost"/>'s own double-promotion guard precedent.
        /// </exception>
        /// <example>
        /// <code>xpTracker.BeginTracking(characterId: 555u, preDisconnectXp: 500);</code>
        /// </example>
        public void BeginTracking(uint characterId, int preDisconnectXp)
        {
            if (_records.ContainsKey(characterId))
            {
                throw new InvalidOperationException(
                    $"[GhostXpPoolTracker] {nameof(BeginTracking)}: characterId={characterId} is already tracked.");
            }

            _records[characterId] = new XpPoolRecord
            {
                PreDisconnectXp = preDisconnectXp,
                PostDisconnectShareXp = 0,
                AccumulationStopped = false,
            };
        }

        /// <summary>
        /// Adds <paramref name="xpAmount"/> to <paramref name="characterId"/>'s post-disconnect party
        /// XP share pool (CR-GH-8.1) -- the passive party-XP awards a ghost continues to receive
        /// while <c>IsGhost=true</c>. No-ops once <see cref="StopAccumulation"/> has been called for
        /// this character -- see class remarks for why this is the AC-GH-18 structural proof, not
        /// merely a guard.
        /// </summary>
        /// <param name="characterId">The tracked ghost character receiving a party XP share.</param>
        /// <param name="xpAmount">The XP amount to add. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="xpAmount"/> is negative.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>xpTracker.AccumulatePostDisconnectShare(characterId: 555u, xpAmount: 40);</code>
        /// </example>
        public void AccumulatePostDisconnectShare(uint characterId, int xpAmount)
        {
            if (xpAmount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(xpAmount),
                    $"[GhostXpPoolTracker] {nameof(AccumulatePostDisconnectShare)}: xpAmount must not be negative (was {xpAmount}).");
            }

            XpPoolRecord record = RequireRecord(characterId, nameof(AccumulatePostDisconnectShare));

            if (record.AccumulationStopped)
            {
                return; // AC-GH-18 / EC-GH-7 -- see class remarks.
            }

            record.PostDisconnectShareXp += xpAmount;
        }

        /// <summary>
        /// Idempotently stops post-disconnect party XP share accumulation for
        /// <paramref name="characterId"/> (AC-GH-18 / EC-GH-7 -- called by
        /// <see cref="PartyDisbandCoordinator.ProcessPartyDisband"/> when the character's party
        /// disbands mid-ghost-period). Safe to call more than once for the same character.
        /// </summary>
        /// <param name="characterId">The tracked character whose accumulation is stopping.</param>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>xpTracker.StopAccumulation(characterId: 555u);</code>
        /// </example>
        public void StopAccumulation(uint characterId)
        {
            RequireRecord(characterId, nameof(StopAccumulation)).AccumulationStopped = true;
        }

        /// <summary>
        /// Returns the banked pre-disconnect XP for <paramref name="characterId"/> (CR-GH-8.1 --
        /// never forfeited).
        /// </summary>
        /// <param name="characterId">The tracked character to query.</param>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>int preDisconnectXp = xpTracker.GetPreDisconnectXp(555u);</code>
        /// </example>
        public int GetPreDisconnectXp(uint characterId)
            => RequireRecord(characterId, nameof(GetPreDisconnectXp)).PreDisconnectXp;

        /// <summary>
        /// Returns the current post-disconnect party XP share pool total for
        /// <paramref name="characterId"/> (CR-GH-8.1 -- the forfeitable pool).
        /// </summary>
        /// <param name="characterId">The tracked character to query.</param>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>int postDisconnectXp = xpTracker.GetPostDisconnectXp(555u);</code>
        /// </example>
        public int GetPostDisconnectXp(uint characterId)
            => RequireRecord(characterId, nameof(GetPostDisconnectXp)).PostDisconnectShareXp;

        /// <summary>
        /// Resolves <paramref name="characterId"/>'s final XP total -- the single CR-GH-9/9.1/9.2
        /// forfeit-vs-restore decision point for this entire story. See class remarks.
        /// </summary>
        /// <param name="characterId">The tracked character to resolve.</param>
        /// <param name="includePostDisconnectShare">
        /// <see langword="false"/> to forfeit the post-disconnect pool (AC-GH-7 TTL expiry, AC-GH-12
        /// ghost death -- CR-GH-9/9.1); <see langword="true"/> to restore it (AC-GH-8 successful
        /// reconnect -- CR-GH-9.2).
        /// </param>
        /// <returns>
        /// <see cref="GetPreDisconnectXp"/> plus <see cref="GetPostDisconnectXp"/> when
        /// <paramref name="includePostDisconnectShare"/> is <see langword="true"/>; otherwise
        /// <see cref="GetPreDisconnectXp"/> alone.
        /// </returns>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>
        /// int forfeited = xpTracker.ResolveFinalXp(555u, includePostDisconnectShare: false); // pre-disconnect only
        /// int restored = xpTracker.ResolveFinalXp(555u, includePostDisconnectShare: true); // pre-disconnect + shares
        /// </code>
        /// </example>
        public int ResolveFinalXp(uint characterId, bool includePostDisconnectShare)
        {
            XpPoolRecord record = RequireRecord(characterId, nameof(ResolveFinalXp));
            return record.PreDisconnectXp + (includePostDisconnectShare ? record.PostDisconnectShareXp : 0);
        }

        /// <summary>
        /// Ends XP-pool tracking for <paramref name="characterId"/>, removing its record entirely.
        /// Called once the ghost-period outcome (forfeit or restore) has been resolved and persisted.
        /// </summary>
        /// <param name="characterId">The tracked character to stop tracking.</param>
        /// <exception cref="InvalidOperationException"><paramref name="characterId"/> is not currently tracked.</exception>
        /// <example>
        /// <code>xpTracker.EndTracking(555u);</code>
        /// </example>
        public void EndTracking(uint characterId)
        {
            RequireRecord(characterId, nameof(EndTracking));
            _records.Remove(characterId);
        }

        /// <summary>
        /// Returns whether <paramref name="characterId"/> currently has an XP-pool record. The only
        /// query on this class that does not throw for an untracked <paramref name="characterId"/> --
        /// it exists specifically to let a caller check before calling a throwing method.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <example>
        /// <code>bool tracked = xpTracker.IsTracked(555u);</code>
        /// </example>
        public bool IsTracked(uint characterId) => _records.ContainsKey(characterId);
    }
}
