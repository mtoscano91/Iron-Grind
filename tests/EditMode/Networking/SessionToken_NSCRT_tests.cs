using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 016 — <see cref="SessionTokenStore"/> (NSCRT),
    /// the first genuinely concurrency-sensitive class in this codebase. Covers all 8 blocking ACs
    /// (AC-TOK-1 through AC-TOK-8).
    /// </summary>
    /// <remarks>
    /// All timing is tick-based/caller-driven except AC-TOK-8's own timing-comparison assertion, which
    /// is a deliberately narrow, story-approved exception to this project's usual
    /// "no time-dependent assertions" rule (see the story file's own resolved note) — it is the one
    /// thing that AC tests. AC-TOK-6's concurrency test uses real parallel execution
    /// (<see cref="Parallel.Invoke(Action[])"/>), not a sequential call-twice simulation, since a
    /// sequential test cannot exercise the compare-and-swap race path at all.
    /// </remarks>
    [TestFixture]
    internal sealed class SessionToken_NSCRT_Tests
    {
        private const uint AccountA = 7u;

        // =========================================================================================
        // AC-TOK-1: Connecting -> Connected issues a non-zero 16-byte token, stored server-side.
        // =========================================================================================

        [Test]
        public void IssueToken_NewAccount_ReturnsNonZero16ByteTokenAndStoresMatchingEntry()
        {
            // Arrange
            var tokenStore = new SessionTokenStore();

            // Act
            byte[] token = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);

            // Assert
            Assert.AreEqual(SessionTokenStore.TOKEN_LENGTH_BYTES, token.Length);
            Assert.IsFalse(SessionTokenStore.IsZeroToken(token), "AC-TOK-1: the issued token must be non-zero.");
            Assert.IsTrue(tokenStore.IsTokenActive(AccountA));

            // The server's stored entry matches: presenting the same token back succeeds.
            bool validated = tokenStore.TryValidateAndRotate(AccountA, token, newExpiresAtTick: 12100u,
                out byte[] newToken, out Guid newSessionId);
            Assert.IsTrue(validated, "AC-TOK-1: the server's ActiveSessionTokens entry must match the issued token.");
        }

        // =========================================================================================
        // AC-TOK-2: correct token on reconnect -> validated (proceeds to Reconnecting), no second
        // auth challenge (i.e. no additional full-auth step is required by this API).
        // =========================================================================================

        [Test]
        public void TryValidateAndRotate_CorrectToken_ReturnsTrueAndRotatesToANewToken()
        {
            // Arrange
            var tokenStore = new SessionTokenStore();
            byte[] originalToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);

            // Act
            bool accepted = tokenStore.TryValidateAndRotate(AccountA, originalToken, newExpiresAtTick: 12100u,
                out byte[] newToken, out Guid newSessionId);

            // Assert — AC-TOK-2: proceeds (no second auth challenge is modeled by this single call
            // succeeding outright).
            Assert.IsTrue(accepted);
            Assert.IsNotNull(newToken);
            Assert.AreEqual(SessionTokenStore.TOKEN_LENGTH_BYTES, newToken.Length);
            Assert.AreNotEqual(newSessionId, Guid.Empty);

            // CR-TOK-5: rotation means the new token differs from the original.
            CollectionAssert.AreNotEqual(originalToken, newToken);
        }

        // =========================================================================================
        // AC-TOK-3: one modified byte -> auth failure; session stays Disconnected_SessionActive
        // (modeled here as: the store is untouched, the ORIGINAL token still validates afterward).
        // =========================================================================================

        [Test]
        public void TryValidateAndRotate_OneByteModified_ReturnsFalseAndOriginalTokenStillValidatesAfterward()
        {
            // Arrange
            var tokenStore = new SessionTokenStore();
            byte[] originalToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);
            byte[] modifiedToken = (byte[])originalToken.Clone();
            modifiedToken[0] ^= 0xFF; // flip one byte

            // Act
            bool accepted = tokenStore.TryValidateAndRotate(AccountA, modifiedToken, newExpiresAtTick: 12100u,
                out byte[] newToken, out Guid newSessionId);

            // Assert
            Assert.IsFalse(accepted, "AC-TOK-3: a single modified byte must fail auth.");
            Assert.IsNull(newToken);

            // AC-TOK-3: "the session remains Disconnected_SessionActive, TTL continues" — modeled as
            // the entry being completely untouched: the ORIGINAL token still validates successfully.
            bool originalStillValid = tokenStore.TryValidateAndRotate(AccountA, originalToken, newExpiresAtTick: 12200u,
                out byte[] rotatedToken, out Guid rotatedSessionId);
            Assert.IsTrue(originalStillValid, "The failed attempt must not have rotated or invalidated the real token.");
        }

        [Test]
        public void TryValidateAndRotate_AccountNotRegistered_ReturnsFalse()
        {
            var tokenStore = new SessionTokenStore();
            byte[] someToken = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];

            bool accepted = tokenStore.TryValidateAndRotate(AccountA, someToken, newExpiresAtTick: 100u,
                out byte[] newToken, out Guid newSessionId);

            Assert.IsFalse(accepted);
            Assert.IsNull(newToken);
        }

        [Test]
        public void TryValidateAndRotate_NullPresentedToken_ThrowsArgumentNullException()
        {
            var tokenStore = new SessionTokenStore();
            tokenStore.IssueToken(AccountA, expiresAtTick: 100u);

            Assert.Throws<ArgumentNullException>(() =>
                tokenStore.TryValidateAndRotate(AccountA, null, newExpiresAtTick: 200u, out _, out _));
        }

        [Test]
        public void TryValidateAndRotate_WrongLengthToken_ReturnsFalseAndEntryUntouched()
        {
            // Arrange — code review coverage gap: no test previously exercised a presented token that
            // isn't exactly TOKEN_LENGTH_BYTES. CryptographicOperations.FixedTimeEquals returns false
            // immediately on a length mismatch (not an exception), so the expected behavior mirrors the
            // one-byte-modified case (AC-TOK-3): auth failure, entry untouched.
            var tokenStore = new SessionTokenStore();
            byte[] realToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);
            byte[] wrongLengthToken = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES - 1];

            // Act
            bool accepted = tokenStore.TryValidateAndRotate(AccountA, wrongLengthToken, newExpiresAtTick: 12100u,
                out byte[] newToken, out Guid newSessionId);

            // Assert
            Assert.IsFalse(accepted);
            Assert.IsNull(newToken);
            Assert.AreEqual(Guid.Empty, newSessionId, "newSessionId must be left at its documented default on a failed call.");

            bool realTokenStillValid = tokenStore.TryValidateAndRotate(AccountA, realToken, newExpiresAtTick: 18100u,
                out byte[] _, out Guid _);
            Assert.IsTrue(realTokenStillValid, "A wrong-length attempt must not have rotated or invalidated the real token.");
        }

        [Test]
        public void IssueToken_CalledTwiceForSameAccount_FirstTokenNoLongerValidates()
        {
            // Arrange — code review coverage gap: the doc comment states IssueToken "unconditionally
            // overwrites any prior entry," but nothing proved the first token stops working.
            var tokenStore = new SessionTokenStore();
            byte[] firstToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);

            // Act
            byte[] secondToken = tokenStore.IssueToken(AccountA, expiresAtTick: 12100u);

            // Assert
            CollectionAssert.AreNotEqual(firstToken, secondToken);

            bool firstTokenStillValid = tokenStore.TryValidateAndRotate(AccountA, firstToken, newExpiresAtTick: 18100u,
                out byte[] _, out Guid _);
            Assert.IsFalse(firstTokenStillValid, "The first token must be invalidated by the second IssueToken call.");

            bool secondTokenValid = tokenStore.TryValidateAndRotate(AccountA, secondToken, newExpiresAtTick: 18100u,
                out byte[] _, out Guid _);
            Assert.IsTrue(secondTokenValid);
        }

        [Test]
        public void TryValidateAndRotate_TokenFromDifferentAccount_DoesNotValidate()
        {
            // Arrange — code review coverage gap: every other test in this file used only one account.
            // This proves cross-account isolation, the analogue of Story 015's per-character dedup
            // scoping test.
            const uint AccountB = 8u;
            var tokenStore = new SessionTokenStore();
            byte[] tokenA = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);
            byte[] tokenB = tokenStore.IssueToken(AccountB, expiresAtTick: 6100u);

            // Act & Assert — account B's real token must not validate against account A's entry, and
            // vice versa.
            bool crossedBAgainstA = tokenStore.TryValidateAndRotate(AccountA, tokenB, newExpiresAtTick: 12100u,
                out byte[] _, out Guid _);
            Assert.IsFalse(crossedBAgainstA);

            bool crossedAAgainstB = tokenStore.TryValidateAndRotate(AccountB, tokenA, newExpiresAtTick: 12100u,
                out byte[] _, out Guid _);
            Assert.IsFalse(crossedAAgainstB);

            // Each account's own token still works.
            Assert.IsTrue(tokenStore.TryValidateAndRotate(AccountA, tokenA, newExpiresAtTick: 18100u, out byte[] _, out Guid _));
            Assert.IsTrue(tokenStore.TryValidateAndRotate(AccountB, tokenB, newExpiresAtTick: 18100u, out byte[] _, out Guid _));
        }

        // =========================================================================================
        // AC-TOK-4: after a successful Reconnecting -> Connected, the pre-reconnect (now-rotated)
        // token fails. Rotation is confirmed ONLY via the old token's invalidation — no direct
        // old-vs-new byte comparison against OnSessionHandshakeEmitted (it has no token field yet,
        // deferred per OQ-NC-SER-2 — see the story file's own resolved note).
        // =========================================================================================

        [Test]
        public void TryValidateAndRotate_PreReconnectTokenResubmittedAfterRotation_Fails()
        {
            // Arrange — a successful reconnect rotates the token.
            var tokenStore = new SessionTokenStore();
            byte[] preReconnectToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);
            bool firstReconnect = tokenStore.TryValidateAndRotate(AccountA, preReconnectToken, newExpiresAtTick: 12100u,
                out byte[] rotatedToken, out Guid rotatedSessionId);
            Assert.IsTrue(firstReconnect, "Sanity check: the pre-reconnect token must succeed once, before rotation.");

            // Act — the client (or an attacker) resubmits the now-rotated (stale) pre-reconnect token.
            bool secondAttempt = tokenStore.TryValidateAndRotate(AccountA, preReconnectToken, newExpiresAtTick: 18100u,
                out byte[] newToken, out Guid newSessionId);

            // Assert — AC-TOK-4: rotation confirmed via the old token's invalidation, not a direct
            // byte comparison against a (nonexistent) handshake token field.
            Assert.IsFalse(secondAttempt, "AC-TOK-4: the pre-reconnect token must fail after rotation.");
            Assert.IsNull(newToken);

            // The currently-valid (rotated) token still works, proving a new token really was issued
            // and stored — this is the "a new token must have been issued and stored" clause.
            bool rotatedTokenStillValid = tokenStore.TryValidateAndRotate(AccountA, rotatedToken, newExpiresAtTick: 24100u,
                out byte[] _, out Guid _);
            Assert.IsTrue(rotatedTokenStillValid);
        }

        // =========================================================================================
        // AC-TOK-5: server restart (empty ActiveSessionTokens) + valid persisted session within TTL
        // -> full auth, new token, character state preserved (character-state preservation is out of
        // scope for this class — it owns only the token; this test proves the empty-store + full-auth
        // + new-token half of the AC).
        // =========================================================================================

        [Test]
        public void IssueToken_AfterSimulatedRestart_EmptyStoreAllowsFreshFullAuthWithNewToken()
        {
            // Arrange — "server restart" is modeled as a fresh SessionTokenStore instance: CR-TOK-8
            // forbids persisting the store to disk, so a restart genuinely starts with an empty
            // ConcurrentDictionary in the real production class too.
            var preRestartStore = new SessionTokenStore();
            byte[] preRestartToken = preRestartStore.IssueToken(AccountA, expiresAtTick: 6100u);

            var postRestartStore = new SessionTokenStore(); // fresh instance = empty ActiveSessionTokens

            // Act — the reconnecting client's stored (pre-restart) token means nothing to the fresh
            // store; a real caller falls back to full authentication and issues a brand-new token.
            Assert.IsFalse(postRestartStore.IsTokenActive(AccountA), "A fresh store has no entries.");
            bool preRestartTokenRejected = postRestartStore.TryValidateAndRotate(AccountA, preRestartToken,
                newExpiresAtTick: 100u, out byte[] _, out Guid _);
            byte[] newToken = postRestartStore.IssueToken(AccountA, expiresAtTick: 12100u);

            // Assert
            Assert.IsFalse(preRestartTokenRejected, "The pre-restart token must not validate against the empty post-restart store.");
            Assert.IsNotNull(newToken);
            Assert.AreEqual(SessionTokenStore.TOKEN_LENGTH_BYTES, newToken.Length);
            Assert.IsTrue(postRestartStore.IsTokenActive(AccountA));
        }

        // =========================================================================================
        // AC-TOK-6: two concurrent ConnectionRequests with the same (old) token -- only the first
        // succeeds; the second (evaluated after rotation) gets auth failure. Real parallel execution,
        // not a sequential simulation -- a sequential test cannot exercise the CAS race path.
        // =========================================================================================

        [Test]
        public void TryValidateAndRotate_TwoConcurrentRequestsWithSameToken_ExactlyOneSucceedsPerIteration()
        {
            const int iterations = 100;

            for (int i = 0; i < iterations; i++)
            {
                // Arrange — a fresh store and token each iteration, so no iteration can interfere with another.
                var tokenStore = new SessionTokenStore();
                byte[] token = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);

                bool resultA = false;
                bool resultB = false;

                // Code review strengthening: both racing threads wait on a 2-party Barrier immediately
                // before calling TryValidateAndRotate, so every iteration deterministically forces both
                // threads to enter the method at the same instant rather than relying on scheduler
                // luck for genuine overlap (previously the two Parallel.Invoke actions could, in
                // principle, run fully sequentially without the test being able to tell the difference).
                using var barrier = new Barrier(2);

                // Act — two threads race to validate-and-rotate the SAME token simultaneously.
                Parallel.Invoke(
                    () =>
                    {
                        barrier.SignalAndWait();
                        resultA = tokenStore.TryValidateAndRotate(AccountA, token, newExpiresAtTick: 12100u, out _, out _);
                    },
                    () =>
                    {
                        barrier.SignalAndWait();
                        resultB = tokenStore.TryValidateAndRotate(AccountA, token, newExpiresAtTick: 12200u, out _, out _);
                    }
                );

                // Assert — exactly one of the two racing calls succeeds; never both, never neither
                // (the token was valid going in, so one of them must win).
                int successCount = (resultA ? 1 : 0) + (resultB ? 1 : 0);
                Assert.AreEqual(1, successCount,
                    $"Iteration {i}: exactly one of two concurrent requests against the same token must succeed (AC-TOK-6). " +
                    $"resultA={resultA}, resultB={resultB}.");
            }
        }

        // =========================================================================================
        // AC-TOK-7: session-stealing invalidates the original token before the new connection's
        // token is issued; the original token fails from that instant.
        // =========================================================================================

        [Test]
        public void InvalidateToken_ThenIssueToken_OriginalTokenFailsFromThatInstant()
        {
            // Arrange
            var tokenStore = new SessionTokenStore();
            byte[] originalToken = tokenStore.IssueToken(AccountA, expiresAtTick: 6100u);

            // Act — session-stealing: invalidate the original BEFORE issuing the new connection's token
            // (the caller's own ordering responsibility — see class remarks).
            tokenStore.InvalidateToken(AccountA);
            byte[] newConnectionToken = tokenStore.IssueToken(AccountA, expiresAtTick: 12100u);

            // Assert — the original token fails immediately.
            bool originalStillWorks = tokenStore.TryValidateAndRotate(AccountA, originalToken, newExpiresAtTick: 18100u,
                out byte[] _, out Guid _);
            Assert.IsFalse(originalStillWorks, "AC-TOK-7: the original token must fail once invalidated by session-stealing.");

            // The new connection's token is valid.
            bool newTokenWorks = tokenStore.TryValidateAndRotate(AccountA, newConnectionToken, newExpiresAtTick: 24100u,
                out byte[] _, out Guid _);
            Assert.IsTrue(newTokenWorks);
        }

        [Test]
        public void InvalidateToken_UnregisteredAccount_NoOp()
        {
            var tokenStore = new SessionTokenStore();

            Assert.DoesNotThrow(() => tokenStore.InvalidateToken(AccountA));
            Assert.IsFalse(tokenStore.IsTokenActive(AccountA));
        }

        // =========================================================================================
        // AC-TOK-8: 1,000 ConnectionRequests with wrong tokens targeting a valid session -- none
        // succeed; no timing difference between match/mismatch comparison paths (p<0.05).
        // Per the story's own resolved test-methodology note: a fixed, deterministic token array
        // (not runtime-random) for reproducibility; the timing-comparison assertion is a narrow,
        // approved exception to this project's "no time-dependent assertions" rule.
        // =========================================================================================

        /// <summary>
        /// A fixed, deterministic array of 1,000 16-byte tokens, none of which equal the real token
        /// used in <see cref="TryProcess_OneThousandWrongTokens_NoneSucceedAndNoTimingLeak_AC_TOK_8"/>.
        /// Generated by a simple index-based formula — not <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
        /// — so this test is fully reproducible across runs, per the story's own resolved note.
        /// </summary>
        private static byte[][] BuildOneThousandDeterministicWrongTokens()
        {
            var tokens = new byte[1000][];
            for (int i = 0; i < 1000; i++)
            {
                var token = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];
                for (int b = 0; b < token.Length; b++)
                {
                    // Deterministic, non-zero, non-repeating-across-i pattern.
                    token[b] = (byte)((i * 31 + b * 17 + 1) & 0xFF);
                }
                tokens[i] = token;
            }
            return tokens;
        }

        [Test]
        public void TryProcess_OneThousandWrongTokens_NoneSucceedAndNoTimingLeak_AC_TOK_8()
        {
            // Arrange
            var tokenStore = new SessionTokenStore();
            byte[] realToken = tokenStore.IssueToken(AccountA, expiresAtTick: 1_000_000u);
            byte[][] wrongTokens = BuildOneThousandDeterministicWrongTokens();

            // Act (a) — none of the 1,000 wrong-token attempts succeed.
            int successCount = 0;
            foreach (byte[] wrongToken in wrongTokens)
            {
                bool accepted = tokenStore.TryValidateAndRotate(AccountA, wrongToken, newExpiresAtTick: 2_000_000u,
                    out byte[] _, out Guid _);
                if (accepted)
                {
                    successCount++;
                }
            }

            // Assert (a)
            Assert.AreEqual(0, successCount, "AC-TOK-8: none of 1,000 wrong-token attempts may succeed.");

            // The real token must still validate — none of the wrong attempts rotated or invalidated it.
            bool realTokenStillValid = tokenStore.TryValidateAndRotate(AccountA, realToken, newExpiresAtTick: 3_000_000u,
                out byte[] _, out Guid _);
            Assert.IsTrue(realTokenStillValid);

            // Act (b) — timing comparison: match-path vs. mismatch-path durations must not differ
            // significantly. Conservative, soft tolerance band (not a real p-value computation, which
            // isn't reliable in a unit test without a statistics library) to avoid CI flakiness: the
            // slower path must not take more than 20x the faster path's mean duration. A genuine
            // non-constant-time comparison (e.g. a naive byte-by-byte early-exit loop) would show a
            // very large, easily-detectable multiple on inputs of this size, not a marginal one — this
            // threshold is deliberately generous to avoid false failures from CI/JIT noise while still
            // catching a real regression to a non-constant-time comparison.
            var freshStore = new SessionTokenStore();
            byte[] freshRealToken = freshStore.IssueToken(AccountA, expiresAtTick: 1_000_000u);

            const int trialsPerGroup = 1000;

            var matchStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < trialsPerGroup; i++)
            {
                CryptographicOperations.FixedTimeEquals(freshRealToken, freshRealToken);
            }
            matchStopwatch.Stop();

            var mismatchStopwatch = Stopwatch.StartNew();
            foreach (byte[] wrongToken in wrongTokens)
            {
                CryptographicOperations.FixedTimeEquals(freshRealToken, wrongToken);
            }
            mismatchStopwatch.Stop();

            double matchMeanTicks = matchStopwatch.ElapsedTicks / (double)trialsPerGroup;
            double mismatchMeanTicks = mismatchStopwatch.ElapsedTicks / (double)wrongTokens.Length;

            double slower = Math.Max(matchMeanTicks, mismatchMeanTicks);
            double faster = Math.Max(1.0, Math.Min(matchMeanTicks, mismatchMeanTicks)); // avoid div-by-zero on a fast timer
            double ratio = slower / faster;

            Assert.Less(ratio, 20.0,
                $"AC-TOK-8: match-path mean ({matchMeanTicks} ticks) vs. mismatch-path mean ({mismatchMeanTicks} ticks) " +
                $"differ by {ratio:F2}x, exceeding the conservative constant-time tolerance band. " +
                "CryptographicOperations.FixedTimeEquals must be used for this comparison, never `==`/SequenceEqual.");
        }

        // =========================================================================================
        // IsZeroToken (CR-TOK-3).
        // =========================================================================================

        [Test]
        public void IsZeroToken_AllZeroBytes_ReturnsTrue()
        {
            var token = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];
            Assert.IsTrue(SessionTokenStore.IsZeroToken(token));
        }

        [Test]
        public void IsZeroToken_OneNonZeroByte_ReturnsFalse()
        {
            var token = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];
            token[15] = 1;
            Assert.IsFalse(SessionTokenStore.IsZeroToken(token));
        }

        [Test]
        public void IsZeroToken_NullToken_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SessionTokenStore.IsZeroToken(null));
        }

        // =========================================================================================
        // IsTokenActive query method.
        // =========================================================================================

        [Test]
        public void IsTokenActive_UnregisteredThenIssuedThenInvalidated_ReflectsStoreState()
        {
            var tokenStore = new SessionTokenStore();

            Assert.IsFalse(tokenStore.IsTokenActive(AccountA));

            tokenStore.IssueToken(AccountA, expiresAtTick: 100u);
            Assert.IsTrue(tokenStore.IsTokenActive(AccountA));

            tokenStore.InvalidateToken(AccountA);
            Assert.IsFalse(tokenStore.IsTokenActive(AccountA));
        }
    }
}
