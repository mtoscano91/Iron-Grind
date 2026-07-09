using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace IronGrind.Currency
{
    /// <summary>
    /// In-memory, server-authoritative implementation of <see cref="ICurrencyService"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Persistence scope note:</b> the GDD states persistence lives in
    /// <c>character_records.gold_balance</c>, owned by Character Persistence (ADR-006).
    /// Character Persistence does not exist yet in this project, so — exactly like
    /// <c>CharacterStats</c> and <c>ItemDatabase</c> before their respective persistence
    /// layers existed — this class holds all state in-memory. Real database wiring is
    /// out of scope for this epic.</para>
    ///
    /// <para><b>Lifecycle:</b> a <see cref="CharacterID"/> must be explicitly registered via
    /// <see cref="RegisterCharacter"/> before <see cref="AddGold"/> or <see cref="TrySpendGold"/>
    /// will operate on it (Story 003) — an unregistered character causes both to fail fast with
    /// <see cref="GoldMutationError.CharacterNotFound"/>, logged server-side via
    /// <see cref="Debug.LogError(object)"/> (GDD EC-CS-1/EC-CS-9: this is a caller bug, not a
    /// player-facing error). <see cref="GetBalance"/> is unaffected by this requirement — it
    /// still falls back to 0 for an absent/unregistered dictionary entry.</para>
    ///
    /// <para><b>Thread safety (Story 004):</b> <see cref="AddGold"/> and <see cref="TrySpendGold"/>
    /// are safe to call concurrently, including concurrently for the SAME <see cref="CharacterID"/>
    /// and/or concurrently across DIFFERENT <see cref="CharacterID"/>s. Storage uses
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> for structural safety across distinct keys,
    /// layered with a per-character <c>lock</c> (see <see cref="GetLockFor"/>) that makes each
    /// character's compound "check version / check balance / write both together" sequence
    /// atomic. <see cref="AddGold"/> uses a single lock around its whole read-modify-write
    /// (never rejects on conflict — only guarantees no lost update). <see cref="TrySpendGold"/>
    /// uses an optimistic version check with a single retry, surfacing
    /// <see cref="GoldMutationError.ConcurrencyConflict"/> after two consecutive version
    /// mismatches (GDD Rule 11; conceptually parallel to ADR-006's database-level optimistic
    /// concurrency, but this is a pure in-memory stand-in — see
    /// <c>docs/architecture/ADR-006-persistence-layer.md</c>).</para>
    /// </remarks>
    public sealed class CurrencySystem : ICurrencyService
    {
        private const uint GOLD_CAP = 9_999_999u;

        private readonly ConcurrentDictionary<CharacterID, uint> _balances = new ConcurrentDictionary<CharacterID, uint>();
        private readonly ConcurrentDictionary<CharacterID, uint> _versions = new ConcurrentDictionary<CharacterID, uint>();
        private readonly ConcurrentDictionary<CharacterID, object> _locks = new ConcurrentDictionary<CharacterID, object>();

        /// <inheritdoc/>
        /// <remarks>
        /// Story 005 (ADR-010 Decision 3, Tier 2 Broadcast Event). Fired from inside the same
        /// per-character lock that produced the new balance/version, immediately before the
        /// success <c>return</c> in <see cref="AddGold"/> and <see cref="TryCompareAndSwapSpend"/>
        /// — never on a guard-rejected path. As the event producer, <see cref="CurrencySystem"/>
        /// does not itself need to implement <see cref="IDisposable"/> (ADR-010 Decision 4's
        /// mandatory-disposal rule applies to subscribers, not producers); a plain
        /// <see cref="Action{T}"/>-backed event already provides native <c>+=</c>/<c>-=</c>
        /// subscription semantics.
        /// </remarks>
        public event Action<GoldSyncEventArgs> OnGoldSync;

        /// <summary>
        /// Returns the lock object serializing compound read-modify-write access to
        /// <paramref name="charId"/>'s balance/version pair. Lazily creates the lock object on
        /// first use via <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>,
        /// which is itself thread-safe and race-free — this correctly handles the case where
        /// this is called for an unregistered <paramref name="charId"/> too (the
        /// CharacterNotFound guard then fires correctly once inside the lock).
        /// </summary>
        private object GetLockFor(CharacterID charId) => _locks.GetOrAdd(charId, _ => new object());

        /// <inheritdoc/>
        /// <remarks>
        /// Direct write — registers <paramref name="charId"/> unconditionally, overwriting any
        /// existing balance. See the interface doc comment for the bootstrap-seam rationale and
        /// why this is not the forbidden general-purpose "set gold" API (GDD Rule 9). Also resets
        /// the character's version counter to 0 (Story 004) — a fresh registration is a fresh
        /// version epoch.
        ///
        /// <para><b>Not safe to call concurrently with <see cref="AddGold"/>/<see cref="TrySpendGold"/>
        /// on the same <paramref name="charId"/> (Story 004):</b> unlike those two methods, this
        /// write does not take the per-character lock (see <see cref="GetLockFor"/>). A concurrent
        /// re-registration of an already-active character (e.g. a re-login race) could reset the
        /// version epoch mid-flight, causing a spurious conflict-detection loss on an in-flight
        /// mutation. This is acceptable for the intended one-time bootstrap use (register before
        /// any gameplay mutation begins) but callers must not treat it as safe for concurrent
        /// re-registration of a character with in-flight mutations.</para>
        /// </remarks>
        public void RegisterCharacter(CharacterID charId, uint initialBalance)
        {
            _balances[charId] = initialBalance;
            _versions[charId] = 0u;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order (fail fast, first match wins): (1) <see cref="GoldMutationError.CharacterNotFound"/>
        /// if <paramref name="charId"/> was never registered, (2) <see cref="GoldMutationError.InvalidAmount"/>
        /// if <paramref name="amount"/> is zero, (3) F-CS-1 cap-safe addition — subtraction-first
        /// to avoid <see cref="uint"/> overflow: <c>balance + amount</c> can overflow when both
        /// operands are near <c>GOLD_CAP</c>; <c>GOLD_CAP - balance</c> is always safe because
        /// <c>balance</c> is invariantly &lt;= <c>GOLD_CAP</c>.
        ///
        /// <para><b>Thread safety (Story 004):</b> the entire guard-check + read-modify-write
        /// sequence runs inside a single per-character lock (see <see cref="GetLockFor"/>) — a
        /// single atomic expression with no prior version read, per GDD Rule 11. This is
        /// sufficient because <see cref="AddGold"/> never rejects on conflict; it only needs to
        /// guarantee no concurrent caller's contribution is lost. The version counter still
        /// increments by exactly 1 on every successful call so that <see cref="TrySpendGold"/>'s
        /// optimistic check correctly detects an interleaved <see cref="AddGold"/> as a conflict.
        /// </para>
        /// </remarks>
        public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
        {
            lock (GetLockFor(charId))
            {
                if (!_balances.ContainsKey(charId))
                {
                    Debug.LogError($"[CurrencySystem] AddGold: {charId} is not a registered character. Call RegisterCharacter before mutating gold.");
                    return new GoldMutationResult(
                        success: false,
                        newBalance: 0u,
                        previousBalance: 0u,
                        error: GoldMutationError.CharacterNotFound);
                }

                if (amount == 0u)
                {
                    Debug.LogError($"[CurrencySystem] AddGold: amount must be nonzero (charId {charId}).");
                    uint currentBalance = _balances[charId];
                    return new GoldMutationResult(
                        success: false,
                        newBalance: currentBalance,
                        previousBalance: currentBalance,
                        error: GoldMutationError.InvalidAmount);
                }

                uint previousBalance = _balances[charId];
                uint newBalance = (amount > GOLD_CAP - previousBalance) ? GOLD_CAP : previousBalance + amount;

                _balances[charId] = newBalance;
                _versions[charId] = _versions[charId] + 1u;
                uint newVersion = _versions[charId];

                OnGoldSync?.Invoke(new GoldSyncEventArgs(charId, newBalance, newVersion, reason));

                return new GoldMutationResult(
                    success: true,
                    newBalance: newBalance,
                    previousBalance: previousBalance,
                    error: GoldMutationError.None);
            }
        }

        /// <inheritdoc/>
        public uint GetBalance(CharacterID charId)
        {
            return _balances.TryGetValue(charId, out uint balance) ? balance : 0u;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>Thread safety (Story 004):</b> this is a thin retry wrapper around the
        /// optimistic compare-and-swap primitive <see cref="TryCompareAndSwapSpend"/> (GDD Rule
        /// 11, first paragraph). It reads the character's current version with no lock held
        /// (a deliberate race window — this mirrors the database's
        /// <c>SELECT version ... ; UPDATE ... WHERE version = @expected</c> pattern from
        /// ADR-006), attempts the CAS, and retries exactly once more if the first attempt
        /// reports <see cref="GoldMutationError.ConcurrencyConflict"/>. If the second attempt
        /// also conflicts, <see cref="GoldMutationError.ConcurrencyConflict"/> is surfaced to the
        /// caller. Success, CharacterNotFound, InvalidAmount, and InsufficientFunds are all final
        /// answers returned immediately without a retry.</para>
        ///
        /// <para>Guard order inside the CAS primitive (fail fast, first match wins): (1)
        /// <see cref="GoldMutationError.CharacterNotFound"/>, (2)
        /// <see cref="GoldMutationError.InvalidAmount"/> if <paramref name="cost"/> is zero, (3)
        /// version mismatch -&gt; <see cref="GoldMutationError.ConcurrencyConflict"/>, (4) F-CS-2
        /// spend guard — guard-before-subtract to avoid <see cref="uint"/> underflow:
        /// <c>balance - cost</c> would wrap to a huge value if <c>cost &gt; balance</c>; checking
        /// <c>balance &lt; cost</c> and returning immediately (before touching the balance store)
        /// guarantees every failure path never writes to the balance store.</para>
        /// </remarks>
        public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint expectedVersion = _versions.TryGetValue(charId, out uint v) ? v : 0u; // no lock held here — deliberate race window
                var result = TryCompareAndSwapSpend(charId, cost, reason, expectedVersion);
                if (result.Error != GoldMutationError.ConcurrencyConflict)
                    return result; // Success, CharacterNotFound, InvalidAmount, or InsufficientFunds are all final answers
            }

            return new GoldMutationResult(
                success: false,
                newBalance: 0u,
                previousBalance: 0u,
                error: GoldMutationError.ConcurrencyConflict);
        }

        /// <summary>
        /// The atomic compare-and-swap primitive backing <see cref="TrySpendGold"/> (GDD Rule 11).
        /// Checks <paramref name="expectedVersion"/> against the character's current version
        /// under a per-character lock; on a match, applies the F-CS-2 spend guard and, if the
        /// balance is sufficient, writes the new balance and increments the version atomically.
        /// </summary>
        /// <remarks>
        /// <b>Why <see langword="internal"/> rather than <see langword="private"/>:</b> per
        /// <c>.claude/rules/test-standards.md</c>, tests must be deterministic — real
        /// thread-scheduling races cannot reliably produce
        /// <see cref="GoldMutationError.ConcurrencyConflict"/> on demand. AC-CS-D-02 requires
        /// forcing this outcome deterministically by calling this method directly with a
        /// deliberately stale <paramref name="expectedVersion"/>.
        /// <c>InternalsVisibleTo("IronGrind.Foundation.EditModeTests")</c> is declared in
        /// <c>src/Foundation/AssemblyInfo.cs</c> and covers the <c>IronGrind.Foundation</c>
        /// assembly Currency System lives in.
        /// </remarks>
        /// <param name="charId">The character spending gold. Must be registered.</param>
        /// <param name="cost">Amount to debit. Must be nonzero.</param>
        /// <param name="reason">Audit reason for this debit.</param>
        /// <param name="expectedVersion">
        /// The version the caller last observed. If it no longer matches the character's current
        /// version, another mutation interleaved and this call reports
        /// <see cref="GoldMutationError.ConcurrencyConflict"/> without touching the balance.
        /// </param>
        internal GoldMutationResult TryCompareAndSwapSpend(CharacterID charId, uint cost, GoldTransactionReason reason, uint expectedVersion)
        {
            lock (GetLockFor(charId))
            {
                if (!_balances.ContainsKey(charId))
                {
                    Debug.LogError($"[CurrencySystem] TrySpendGold: {charId} is not a registered character. Call RegisterCharacter before mutating gold.");
                    return new GoldMutationResult(
                        success: false,
                        newBalance: 0u,
                        previousBalance: 0u,
                        error: GoldMutationError.CharacterNotFound);
                }

                if (cost == 0u)
                {
                    Debug.LogError($"[CurrencySystem] TrySpendGold: cost must be nonzero (charId {charId}).");
                    uint currentBalance = _balances[charId];
                    return new GoldMutationResult(
                        success: false,
                        newBalance: currentBalance,
                        previousBalance: currentBalance,
                        error: GoldMutationError.InvalidAmount);
                }

                if (_versions[charId] != expectedVersion)
                {
                    uint currentBalance = _balances[charId];
                    return new GoldMutationResult(
                        success: false,
                        newBalance: currentBalance,
                        previousBalance: currentBalance,
                        error: GoldMutationError.ConcurrencyConflict);
                }

                uint balance = _balances[charId];

                if (balance < cost)
                {
                    return new GoldMutationResult(
                        success: false,
                        newBalance: balance,
                        previousBalance: balance,
                        error: GoldMutationError.InsufficientFunds);
                }

                uint newBalance = balance - cost;
                _balances[charId] = newBalance;
                _versions[charId] = _versions[charId] + 1u;
                uint newVersion = _versions[charId];

                OnGoldSync?.Invoke(new GoldSyncEventArgs(charId, newBalance, newVersion, reason));

                return new GoldMutationResult(
                    success: true,
                    newBalance: newBalance,
                    previousBalance: balance,
                    error: GoldMutationError.None);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Story 006 stub. GDD Rule 8: not implemented at MVP — deferred to the Trade System
        /// GDD (outside the 35-system MVP scope). Deliberately touches neither
        /// <paramref name="fromId"/>'s nor <paramref name="toId"/>'s balance, and performs no
        /// registration lookup or amount validation — this is a placeholder, not a partial
        /// implementation.
        /// </remarks>
        public GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount)
        {
            return new GoldMutationResult(success: false, newBalance: 0u, previousBalance: 0u, error: GoldMutationError.NotImplemented);
        }
    }
}
