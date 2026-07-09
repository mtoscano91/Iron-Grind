using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 005 — the RFC 1982 serial-number-arithmetic
    /// stale-discard comparison helpers (<see cref="StaleDiscardComparer.IsNewerVersion"/>,
    /// <see cref="StaleDiscardComparer.IsTickExpired"/>). Covers AC-NC-07 (GoldSyncEvent
    /// stale-discard scenarios), AC-NC-36 (<c>SequenceNumber</c> wraparound via
    /// <see cref="TransportFaultInjector"/>, whose <c>ConsumeNextSequenceNumber</c> zero-skip bug
    /// this story also fixes), and AC-WP-2 (<c>IsTickExpired</c> equality/wraparound semantics).
    /// </summary>
    /// <remarks>
    /// <b>AC-NC-36 numbering discrepancy (flagged for story-text correction):</b> the story's AC
    /// text states that seeding <c>SetSequenceNumber(4294967293)</c> and emitting 5 messages
    /// produces the sequence <c>4294967294 → 4294967295 → 1 → 2 → 3</c>. But
    /// <see cref="ITransportFaultInjector.SetSequenceNumber"/>'s own already-reviewed XML doc
    /// comment defines its parameter as "the value the outbound sequence counter will resume
    /// from" — i.e. the value <see cref="TransportFaultInjector.ConsumeNextSequenceNumber"/>
    /// returns on its very next call, not the value after it (this "resume from" contract is also
    /// exercised, unchanged, by Story 001's
    /// <c>SetSequenceNumber_CalledBeforeAnyMessageEmitted_AppliesImmediatelyAndWrapsSkippingZero</c>
    /// test). Under that documented contract, the correct 5-value sequence is
    /// <c>4294967293 → 4294967294 → 4294967295 → 1 → 2</c> (with the zero-skip fix applied). This
    /// test asserts the corrected sequence, trusting the already-reviewed harness contract over
    /// the AC's literal printed numbers.
    /// </remarks>
    [TestFixture]
    internal sealed class WireProtocol_StaleDiscard_Tests
    {
        // -----------------------------------------------------------------------
        // AC-NC-07: GoldSyncEvent stale-discard scenarios — IsNewerVersion proven
        // directly at the comparison boundary (no GoldSyncEvent receiver/cache
        // class exists yet in this codebase; out of scope for this story).
        // -----------------------------------------------------------------------

        [Test]
        public void IsNewerVersion_ReorderedOlderVersionArrivesAfterNewer_ReturnsFalse()
        {
            // Arrange — Version=6 (600g) was already applied; Version=5 (500g) arrives late.
            const uint appliedVersion = 6;
            const uint lateArrivingVersion = 5;

            // Act
            bool isNewer = StaleDiscardComparer.IsNewerVersion(current: appliedVersion, candidate: lateArrivingVersion);

            // Assert — 5 must be discarded as stale; display stays at 600g/Version 6.
            Assert.IsFalse(isNewer, "A lower Version arriving after a higher Version was applied must be discarded as stale.");
        }

        [Test]
        public void IsNewerVersion_WraparoundVersionArrivesAfterMaxValue_ReturnsTrueAndContrastsRawComparison()
        {
            // Arrange — Version=4,294,967,295 (500g) was already applied; Version=1 (600g) arrives
            // next, wrapping past uint.MaxValue.
            const uint appliedVersion = uint.MaxValue;
            const uint wrappedVersion = 1;

            // Act
            bool isNewer = StaleDiscardComparer.IsNewerVersion(current: appliedVersion, candidate: wrappedVersion);

            // Assert — RFC-1982 arithmetic correctly recognizes 1 as newer despite wraparound.
            Assert.IsTrue(isNewer, "Version=1 arriving after Version=uint.MaxValue must be recognized as newer via wraparound-safe arithmetic.");

            // Assert — explicit contrast: a raw uint comparison gets this backwards, which is
            // exactly why IsNewerVersion (not a plain '>') must be used for this check.
            Assert.IsFalse(wrappedVersion > appliedVersion, "Raw comparison '1 > 4294967295' is false — proves a plain comparison would incorrectly discard the newer value.");
        }

        [Test]
        public void IsNewerVersion_OrdinaryNonWrappingGap_ReturnsTrue()
        {
            // Arrange — an ordinary far-apart pair nowhere near the wraparound boundary.
            const uint current = 100;
            const uint candidate = 50000;

            // Act
            bool isNewer = StaleDiscardComparer.IsNewerVersion(current, candidate);

            // Assert
            Assert.IsTrue(isNewer, "An ordinary, non-wrapping later value must be recognized as newer.");
        }

        [Test]
        public void IsNewerVersion_ExactlyHalfCircleApart_ResolvesFalse()
        {
            // Arrange — RFC 1982's "undefined" boundary: the two values are exactly 0x80000000
            // apart, the exact half-circle point where the comparison direction is ambiguous by
            // spec. This project's chosen (and now pinned) resolution: false — not newer.
            const uint current = 0;
            const uint candidate = 0x80000000u;

            // Act
            bool isNewer = StaleDiscardComparer.IsNewerVersion(current, candidate);

            // Assert — pins the current implementation's resolution so a future refactor cannot
            // silently flip it without this test failing.
            Assert.IsFalse(isNewer, "At exactly 0x80000000 apart (RFC 1982's ambiguous half-circle boundary), this implementation resolves to false.");
        }

        [Test]
        public void IsTickExpired_ExactlyHalfCircleApart_ResolvesFalse()
        {
            // Arrange — same RFC 1982 ambiguous boundary as above, for IsTickExpired.
            const uint currentTick = 0;
            const uint expiryTick = 0x80000000u;

            // Act
            bool isExpired = StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);

            // Assert — pins the current implementation's resolution at the ambiguous boundary.
            Assert.IsFalse(isExpired, "At exactly 0x80000000 apart (RFC 1982's ambiguous half-circle boundary), this implementation resolves to false (not expired).");
        }

        // -----------------------------------------------------------------------
        // AC-NC-36: SequenceNumber wraparound via TransportFaultInjector.
        // SetSequenceNumber(4294967293) + 5 consecutive ConsumeNextSequenceNumber()
        // calls. See class remarks for the AC numbering discrepancy this test
        // resolves in favor of the already-reviewed SetSequenceNumber contract.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeNextSequenceNumber_SeededNearMaxValue_WrapsSkippingZeroAcrossFiveEmits()
        {
            // Arrange
            var injector = new TransportFaultInjector();
            injector.SetSequenceNumber(4294967293u);
            var observed = new List<uint>(5);

            // Act — 5 consecutive emits.
            for (int i = 0; i < 5; i++)
            {
                observed.Add(injector.ConsumeNextSequenceNumber());
            }

            // Assert — corrected sequence per the SetSequenceNumber "resume from" contract:
            // resumes at 4294967293 itself, wraps past uint.MaxValue, and skips 0 entirely.
            var expected = new List<uint> { 4294967293u, 4294967294u, 4294967295u, 1u, 2u };
            CollectionAssert.AreEqual(expected, observed, "Sequence must resume from the seeded value, wrap past uint.MaxValue, and skip 0.");

            // Assert — 0 never appears in the captured sequence (CR-NET-7.5: 0 is reserved as
            // "uninitialized" and must never appear in a valid message).
            Assert.IsFalse(observed.Contains(0u), "SequenceNumber = 0 must never appear in any captured message.");

            // Assert — every successive value in the captured sequence is confirmed newer than the
            // previous via IsNewerVersion (the receiver's actual stale-discard check).
            for (int i = 1; i < observed.Count; i++)
            {
                Assert.IsTrue(
                    StaleDiscardComparer.IsNewerVersion(current: observed[i - 1], candidate: observed[i]),
                    $"observed[{i}]={observed[i]} must be recognized as newer than observed[{i - 1}]={observed[i - 1]}.");
            }
        }

        // -----------------------------------------------------------------------
        // AC-WP-2: IsTickExpired equality/wraparound semantics, plus the explicit
        // contrast with IsNewerVersion's opposite equality behavior.
        // -----------------------------------------------------------------------

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(1000u)]
        [TestCase(uint.MaxValue)]
        public void IsTickExpired_CurrentTickEqualsExpiryTick_ReturnsTrue(uint tick)
        {
            // Act
            bool isExpired = StaleDiscardComparer.IsTickExpired(currentTick: tick, expiryTick: tick);

            // Assert — equality = expired (opposite of IsNewerVersion's equality behavior).
            Assert.IsTrue(isExpired, $"IsTickExpired must return true at equality (tick={tick}).");
        }

        [Test]
        public void IsTickExpired_CurrentTickWrappedPastMaxValue_ReturnsTrue()
        {
            // Arrange — expiryTick is 2 ticks before uint.MaxValue; currentTick has wrapped around
            // to 3, a small delta past the wraparound boundary.
            const uint expiryTick = uint.MaxValue - 2;
            const uint currentTick = 3;

            // Act
            bool isExpired = StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);

            // Assert
            Assert.IsTrue(isExpired, "A currentTick that has wrapped past uint.MaxValue relative to expiryTick must still report expired.");
        }

        [Test]
        public void IsTickExpired_CurrentTickWellBeforeExpiryTick_ReturnsFalse()
        {
            // Arrange
            const uint currentTick = 100;
            const uint expiryTick = 5000;

            // Act
            bool isExpired = StaleDiscardComparer.IsTickExpired(currentTick, expiryTick);

            // Assert
            Assert.IsFalse(isExpired, "A currentTick well before expiryTick must not be reported as expired.");
        }

        [Test]
        public void IsNewerVersionAndIsTickExpired_AtEquality_HaveOppositeSemantics()
        {
            // Arrange — the same input pattern (equal values) fed to both functions.
            const uint sameValue = 42;

            // Act
            bool isNewer = StaleDiscardComparer.IsNewerVersion(current: sameValue, candidate: sameValue);
            bool isExpired = StaleDiscardComparer.IsTickExpired(currentTick: sameValue, expiryTick: sameValue);

            // Assert — explicit contrast: IsNewerVersion says "not newer" at equality (duplicate
            // discarded), IsTickExpired says "expired" at equality (TTL reached exactly now).
            Assert.IsFalse(isNewer, "IsNewerVersion must return false at equality — a duplicate value is not newer.");
            Assert.IsTrue(isExpired, "IsTickExpired must return true at equality — a TTL reached exactly now has expired.");
        }
    }
}
