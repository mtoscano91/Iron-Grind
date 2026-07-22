using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace IronGrind.Networking
{
    /// <summary>
    /// In-process, non-persisted store for the NSCRT session token (<c>networking-session-token.md</c>,
    /// CR-TOK-1 through CR-TOK-8) — the credential a reconnecting client presents to skip full
    /// re-authentication within the session TTL. This is the first genuinely concurrency-sensitive
    /// class in this codebase: every prior Networking Core registry (<see cref="ConnectionStateMachine"/>,
    /// <see cref="ZoneSessionStateMachine"/>) is a plain <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
    /// and implicitly single-threaded, but CR-TOK-8 requires thread safety because reconnect handlers
    /// and TTL expiry can execute concurrently at the 20Hz tick boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="ActiveTokenEntry"/> is immutable — a deliberate, necessary departure from this
    /// epic's usual mutable-record convention:</b> <see cref="ConnectionStateMachine.AccountSessionRecord"/>
    /// and <see cref="ZoneSessionStateMachine"/>'s own nested record mutate an existing instance's
    /// fields in place. This class cannot do that: <see cref="TryValidateAndRotate"/>'s atomicity
    /// guarantee for AC-TOK-6 (only the first of two concurrent requests succeeds) depends on
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate(TKey,TValue,TValue)"/>'s reference-identity
    /// compare-and-swap semantics — it compares the *comparison value* against what is currently stored
    /// by reference equality. If <see cref="ActiveTokenEntry"/> were mutated in place, two threads
    /// racing on the same entry would both see (and successfully "compare-and-swap" against) the same
    /// mutated reference, defeating the whole point of the CAS. Every entry is therefore a fresh,
    /// all-<see langword="readonly"/> instance, replaced wholesale by <see cref="TryUpdate"/>, never
    /// mutated. Do not "fix" this back to a mutable record — it would silently break AC-TOK-6.
    /// </para>
    /// <para>
    /// <b><see cref="TryValidateAndRotate"/> is the single atomic validate-and-rotate operation
    /// (AC-TOK-2/3/4/6):</b> a lookup miss or a token mismatch returns <see langword="false"/> with the
    /// entry left completely untouched — this is what makes AC-TOK-3 correct (a wrong-byte attempt
    /// never rotates the real token, so the legitimate client's stored token stays valid). A match
    /// attempts a <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate(TKey,TValue,TValue)"/>
    /// compare-and-swap against the exact entry instance just read. If the CAS fails — another thread
    /// already rotated this same entry between this thread's read and this write, i.e. this thread lost
    /// the race — that is treated as a failed validation too, returning <see langword="false"/> rather
    /// than retrying. This is exactly AC-TOK-6's contract: "two concurrent <c>ConnectionRequest</c>s ...
    /// only the first succeeds ... the second returns auth failure." No external locking is needed at
    /// any call site; the atomicity is structural.
    /// </para>
    /// <para>
    /// <b><see cref="IssueToken"/> is not concurrency-hardened, by design:</b> it unconditionally
    /// creates or overwrites the entry (mirrors <see cref="ConnectionStateMachine.EnterConnecting"/>'s
    /// own overwrite precedent for a brand-new connection) — used only for a fresh
    /// <c>Connecting → Connected</c> full authentication (AC-TOK-1) or the post-restart full-auth path
    /// (AC-TOK-5), neither of which has anything valid to race against.
    /// </para>
    /// <para>
    /// <b><see cref="IsZeroToken"/> deliberately does not use <see cref="CryptographicOperations.FixedTimeEquals"/>:</b>
    /// CR-TOK-3's zero-filled sentinel is a public, known value, not a secret — comparing against it
    /// carries no timing-oracle risk, unlike <see cref="TryValidateAndRotate"/>'s comparison against
    /// the actual stored token (CR-TOK-4, AC-TOK-8).
    /// </para>
    /// <para>
    /// <b><c>REAUTH_FAILURE_LIMIT</c> is out of scope for this class:</b> AC-TOK-8's "1,000
    /// <c>ConnectionRequest</c>s ... <c>REAUTH_FAILURE_LIMIT</c> triggers before any succeed" composes
    /// two independent mechanisms — this class only needs to prove that 1,000 wrong-token attempts
    /// against one valid session all fail (via <see cref="TryValidateAndRotate"/>) with no timing leak;
    /// the actual failure-count lockout is <see cref="ConnectionStateMachine.RecordFailedReAuthAttempt"/>'s
    /// existing job (Story 013), which a real caller composes with this class, not something this class
    /// itself tracks.
    /// </para>
    /// <para>
    /// <b>Storage (CR-TOK-8):</b> <see cref="ConcurrentDictionary{TKey,TValue}"/>, in-process only —
    /// never persisted to disk or a distributed cache, per CR-TOK-8's explicit prohibition. A server
    /// restart therefore clears every entry, which is the correct, GDD-mandated behavior for AC-TOK-5
    /// (a reconnecting client must complete full re-authentication after a restart, not skip it via a
    /// stale token).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tokenStore = new SessionTokenStore();
    ///
    /// // Connecting -> Connected: full auth issues a fresh token.
    /// byte[] token = tokenStore.IssueToken(accountId: 7, expiresAtTick: 6100u);
    ///
    /// // Reconnect: the client presents its stored token.
    /// bool accepted = tokenStore.TryValidateAndRotate(accountId: 7, presentedToken: token,
    ///     newExpiresAtTick: 12100u, out byte[] newToken, out Guid newSessionId);
    /// // accepted == true; newToken != token (rotated); the OLD `token` value now fails.
    ///
    /// // Session-stealing: invalidate before issuing the new connection's token.
    /// tokenStore.InvalidateToken(accountId: 7);
    /// byte[] stolenSessionToken = tokenStore.IssueToken(accountId: 7, expiresAtTick: 6200u);
    /// </code>
    /// </example>
    public sealed class SessionTokenStore
    {
        /// <summary>Fixed token length in bytes (CR-TOK-1, F-TOK-1's 128-bit entropy).</summary>
        public const int TOKEN_LENGTH_BYTES = 16;

        /// <summary>
        /// One account's active session token entry. Immutable by design — see class remarks for why
        /// this must never become a mutate-in-place record.
        /// </summary>
        private sealed class ActiveTokenEntry
        {
            internal readonly byte[] Token;
            internal readonly Guid SessionId;
            internal readonly uint ExpiresAtTick;

            internal ActiveTokenEntry(byte[] token, Guid sessionId, uint expiresAtTick)
            {
                Token = token;
                SessionId = sessionId;
                ExpiresAtTick = expiresAtTick;
            }
        }

        private readonly ConcurrentDictionary<uint, ActiveTokenEntry> _activeSessionTokens = new();

        /// <summary>
        /// Generates <paramref name="lengthBytes"/> cryptographically-random bytes for a fresh or
        /// rotated token. Uses the <see cref="RandomNumberGenerator.Create()"/> factory + instance
        /// <see cref="RandomNumberGenerator.GetBytes(byte[])"/> pattern rather than the static
        /// <c>RandomNumberGenerator.GetBytes(int)</c> overload (.NET 6+) — Unity's scripting runtime
        /// does not expose that overload, and calling it here previously failed to compile
        /// (<c>CS1503: cannot convert from 'int' to 'byte[]'</c>, the compiler resolving against the
        /// instance <c>GetBytes(byte[])</c> overload instead). Found and fixed during Story 028, the
        /// first time this codebase actually compiled against a live Unity Editor.
        /// </summary>
        private static byte[] GenerateToken(int lengthBytes)
        {
            byte[] token = new byte[lengthBytes];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(token);
            }

            return token;
        }

        /// <summary>
        /// Issues a brand-new token for <paramref name="accountId"/>, unconditionally overwriting any
        /// prior entry (ST-NET-1 <c>Connecting → Connected</c> full authentication, AC-TOK-1; also used
        /// for the post-restart full-auth path, AC-TOK-5). Not concurrency-hardened — see class remarks
        /// for why a fresh issuance has nothing valid to race against.
        /// </summary>
        /// <param name="accountId">The account completing full authentication.</param>
        /// <param name="expiresAtTick">
        /// The absolute tick at which this token's session TTL expires — caller-supplied, never
        /// computed by this class, matching <see cref="ConnectionStateMachine"/>'s own
        /// "caller-supplied timeout tick counts, never stored as constants here" precedent.
        /// </param>
        /// <returns>The newly generated 16-byte token (CR-TOK-1), delivered to the client in <c>SessionHandshake</c>.</returns>
        /// <example>
        /// <code>byte[] token = tokenStore.IssueToken(accountId: 7, expiresAtTick: 6100u);</code>
        /// </example>
        public byte[] IssueToken(uint accountId, uint expiresAtTick)
        {
            byte[] token = GenerateToken(TOKEN_LENGTH_BYTES);
            var entry = new ActiveTokenEntry(token, Guid.NewGuid(), expiresAtTick);
            _activeSessionTokens[accountId] = entry;
            return token;
        }

        /// <summary>
        /// Atomically validates <paramref name="presentedToken"/> against <paramref name="accountId"/>'s
        /// stored token and, only on a match, rotates to a freshly generated token (CR-TOK-4, CR-TOK-5;
        /// AC-TOK-2, AC-TOK-3, AC-TOK-4, AC-TOK-6, AC-TOK-8). See class remarks for the full
        /// compare-and-swap correctness argument.
        /// </summary>
        /// <param name="accountId">The account attempting to reconnect.</param>
        /// <param name="presentedToken">
        /// The 16-byte token the client presented in its <c>ConnectionRequest</c>. Must not be
        /// <see langword="null"/>. A caller should check <see cref="IsZeroToken"/> first — a zero-filled
        /// token means "fresh connection, skip this method entirely, do full auth via
        /// <see cref="IssueToken"/> instead" (CR-TOK-3).
        /// </param>
        /// <param name="newExpiresAtTick">
        /// The absolute tick at which the newly rotated token's session TTL expires, if this call
        /// succeeds. Caller-supplied, never computed by this class.
        /// </param>
        /// <param name="newToken">The freshly rotated token, if this call returns <see langword="true"/>; otherwise <see langword="null"/>.</param>
        /// <param name="newSessionId">The freshly generated per-session audit-correlation GUID (CR-TOK-7), if this call returns <see langword="true"/>; otherwise <see langword="default"/>.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="accountId"/> has an active entry, <paramref name="presentedToken"/>
        /// matches it (via <see cref="CryptographicOperations.FixedTimeEquals"/>), and this call won the
        /// compare-and-swap race to rotate it. <see langword="false"/> otherwise — no entry, a mismatched
        /// token (entry left untouched), or a lost race against a concurrent caller (entry already
        /// rotated by the winner).
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="presentedToken"/> is <see langword="null"/>.</exception>
        /// <example>
        /// <code>
        /// bool accepted = tokenStore.TryValidateAndRotate(accountId: 7, presentedToken: clientToken,
        ///     newExpiresAtTick: 12100u, out byte[] newToken, out Guid newSessionId);
        /// </code>
        /// </example>
        public bool TryValidateAndRotate(uint accountId, byte[] presentedToken, uint newExpiresAtTick,
            out byte[] newToken, out Guid newSessionId)
        {
            if (presentedToken == null)
            {
                throw new ArgumentNullException(nameof(presentedToken));
            }

            if (!_activeSessionTokens.TryGetValue(accountId, out ActiveTokenEntry currentEntry))
            {
                newToken = null;
                newSessionId = default;
                return false;
            }

            // CR-TOK-4: constant-time comparison — never `==` or SequenceEqual (timing-oracle risk).
            if (!CryptographicOperations.FixedTimeEquals(presentedToken, currentEntry.Token))
            {
                newToken = null;
                newSessionId = default;
                return false; // entry left completely untouched — see class remarks (AC-TOK-3)
            }

            byte[] rotatedToken = GenerateToken(TOKEN_LENGTH_BYTES);
            Guid rotatedSessionId = Guid.NewGuid();
            var rotatedEntry = new ActiveTokenEntry(rotatedToken, rotatedSessionId, newExpiresAtTick);

            // Compare-and-swap against the exact instance read above. A failed CAS means a concurrent
            // caller already won this race (AC-TOK-6) — treat that as a failed validation too, not a retry.
            if (!_activeSessionTokens.TryUpdate(accountId, rotatedEntry, currentEntry))
            {
                newToken = null;
                newSessionId = default;
                return false;
            }

            newToken = rotatedToken;
            newSessionId = rotatedSessionId;
            return true;
        }

        /// <summary>
        /// Removes <paramref name="accountId"/>'s active token entry entirely (CR-TOK-6/invalidation):
        /// TTL expiry, explicit disconnect, or session-stealing. For session-stealing (AC-TOK-7), the
        /// caller must call this BEFORE calling <see cref="IssueToken"/> for the new connection — this
        /// class does not own that ordering, mirroring <see cref="ConnectionStateMachine.HandleSessionSteal"/>'s
        /// own "explicit removal before re-registration, for legibility" precedent.
        /// </summary>
        /// <param name="accountId">The account whose token is being invalidated.</param>
        /// <example><code>tokenStore.InvalidateToken(accountId: 7);</code></example>
        public void InvalidateToken(uint accountId)
        {
            _activeSessionTokens.TryRemove(accountId, out _);
        }

        /// <summary>
        /// Returns whether <paramref name="token"/> is the CR-TOK-3 zero-filled sentinel — "fresh
        /// connection, skip token lookup, do full auth instead." A plain comparison, not
        /// <see cref="CryptographicOperations.FixedTimeEquals"/> — see class remarks for why this
        /// specific comparison carries no timing-oracle risk.
        /// </summary>
        /// <param name="token">The token to check. Must not be <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
        /// <example><code>bool freshConnection = SessionTokenStore.IsZeroToken(presentedToken);</code></example>
        public static bool IsZeroToken(byte[] token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            for (int i = 0; i < token.Length; i++)
            {
                if (token[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Returns whether <paramref name="accountId"/> currently has an active token entry.</summary>
        /// <param name="accountId">The account to query.</param>
        /// <example><code>bool active = tokenStore.IsTokenActive(accountId: 7);</code></example>
        public bool IsTokenActive(uint accountId) => _activeSessionTokens.ContainsKey(accountId);
    }
}
