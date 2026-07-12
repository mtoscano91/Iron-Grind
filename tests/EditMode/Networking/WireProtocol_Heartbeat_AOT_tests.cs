using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 008 — the CR-NET-7.10 <see cref="HeartbeatMessage"/>
    /// schema, its skip-on-activity scheduling tracker (<see cref="HeartbeatActivityTracker"/>), and
    /// the CR-NET-7.8 IL2CPP/AOT static-analysis guardrail. Covers AC-HB-1 (10-byte envelope-only
    /// wire size), AC-NC-38 (skip-on-activity heartbeat-due scheduling), and AC-AOT-1 (lightweight
    /// source-scanning static analysis over <c>src/Foundation/Networking/</c>, with a self-test
    /// proving the scanner correctly flags deliberately-introduced violations).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AC-AOT-1 scanner scope and known imprecision</b> (documented per this project's
    /// established "flag imprecision explicitly rather than hide it" convention, e.g.
    /// <c>tools/ci/check-test-harness-guards.sh</c>'s own documented limitations): the
    /// <see cref="AotGuardScanner"/> nested class below is a single-pass, line-based text scanner —
    /// not a Roslyn/AST-level analyzer. It implements the three checks AC-AOT-1's own acceptance
    /// text names as blocking (<c>typeof(T)</c>-style generic dispatch, <c>BinaryFormatter</c>/
    /// <c>JsonUtility</c> call sites, and lambda-shaped event/delegate registration), plus two
    /// bonus checks drawn from the broader CR-NET-7.8 checklist that are cheap and effectively
    /// zero-false-positive as plain substring matches (<c>System.Reflection.Emit</c> and
    /// <c>Enum.IsDefined</c>). The remainder of the CR-NET-7.8 checklist — interface dispatch on
    /// value types, virtual dispatch on generic value-type parameters, LINQ on hot paths, primitive
    /// serializer generics — is reproduced verbatim below for future implementers but is <i>not</i>
    /// mechanically checked here: distinguishing those requires semantic/type information a
    /// lightweight text scan cannot obtain (e.g. "is this a hot path?", "is <c>T</c> constrained to
    /// <c>struct</c>?"). That subset must be enforced by code review until a real Roslyn analyzer
    /// exists — flagged explicitly rather than silently treated as covered.
    /// </para>
    /// <para>
    /// The <c>typeof(T)</c> check specifically is a heuristic, not a semantic one: it flags
    /// <c>typeof(X)</c> where <c>X</c> starts with an uppercase <c>T</c> immediately followed by
    /// another uppercase letter/digit/underscore (matching this codebase's and .NET's generic
    /// type-parameter naming convention: <c>T</c>, <c>TKey</c>, <c>TValue</c>, <c>TResult</c>, ...)
    /// or is the bare single character <c>T</c>. It will miss a real <c>typeof(T)</c> dispatch
    /// violation that uses a non-conventional type-parameter name, and — in principle — could flag
    /// a concrete (non-generic-parameter) type that happens to be named starting with an uppercase
    /// letter pair like <c>T</c>+uppercase (none currently exist in this codebase's <c>typeof()</c>
    /// call sites; see the "clean fixture" self-test below, which locks in that <c>typeof(int)</c>
    /// and similar ordinary/non-generic-parameter usages are <i>not</i> flagged).
    /// </para>
    /// <para>
    /// The lambda-capture check flags <i>any</i> <c>+=</c> event/delegate registration whose
    /// right-hand side contains a lambda arrow (<c>=&gt;</c>) on the same line, regardless of
    /// whether that specific lambda actually captures a heap object — determining true variable
    /// capture requires a semantic model this lightweight text scan does not have. This
    /// deliberately over-approximates (errs toward false positives) rather than silently missing a
    /// captured closure; a truly capture-free lambda registered via <c>+=</c> would currently be a
    /// rare, easily-refactored-to-a-named-method false positive. Multi-line <c>+=</c> statements or
    /// multi-line lambda bodies split across lines are not detected — the same single-line
    /// limitation this codebase's <c>check-test-harness-guards.sh</c> already documents for its own
    /// conditional-compilation scan.
    /// </para>
    /// <para>
    /// <b>The full CR-NET-7.8 forbidden-construct checklist</b> (for future wire-message
    /// implementers in this folder — a natural home for this checklist per the story's own
    /// Implementation Notes):
    /// </para>
    /// <list type="bullet">
    /// <item><description>No generic serializers using <c>typeof(T)</c> dispatch for value types. [checked, heuristically]</description></item>
    /// <item><description>No <c>[StructLayout(LayoutKind.Explicit)]</c> without device testing. [not checked — requires device testing sign-off, not a text pattern]</description></item>
    /// <item><description>No <c>BinaryFormatter</c> or <c>JsonUtility</c>. [checked]</description></item>
    /// <item><description>Avoid boxing value types on the hot path. [not checked — requires knowing which paths are "hot"]</description></item>
    /// <item><description><c>enum : byte</c> range validation must use explicit guards — <c>Enum.IsDefined()</c> is forbidden. [checked]</description></item>
    /// <item><description>Interface dispatch on value types boxes at each call site on IL2CPP — use concrete delegates, not interfaces on structs. [not checked]</description></item>
    /// <item><description>Virtual/interface dispatch on a generic value-type parameter (<c>struct MessageBuffer&lt;T&gt; where T : struct</c> calling a virtual method on <c>T</c>) fails on IL2CPP while working in the Editor. [not checked]</description></item>
    /// <item><description><c>System.Reflection.Emit</c> is unavailable on IL2CPP. [checked]</description></item>
    /// <item><description>LINQ (<c>Where</c>/<c>Select</c>/<c>OrderBy</c>) is forbidden on the message dispatch and serialization hot paths — use index-based loops. [not checked — requires knowing which paths are "hot"]</description></item>
    /// <item><description>Lambda capture on handler registration allocates on every registration — register named method delegates instead. [checked, heuristically]</description></item>
    /// <item><description>Primitive field serializers must be concrete non-generic methods with no runtime type dispatch. [not checked — overlaps with the typeof(T) heuristic but not fully covered]</description></item>
    /// </list>
    /// <para>
    /// <b>Line-comment blindness (intentional):</b> the scanner skips any line whose trimmed text
    /// begins with <c>//</c> (including <c>///</c> XML doc-comment lines) before applying any
    /// forbidden-pattern regex. This is required, not merely convenient: this exact folder's own
    /// doc comments (e.g. <c>DisconnectType.cs</c>, <c>DamageType.cs</c>, <c>WireEnumCodec.cs</c>)
    /// legitimately reference <c>&lt;see cref="System.Enum.IsDefined(System.Type, object)"/&gt;</c>
    /// to explain <i>why</i> it is forbidden — without the full-line comment skip, the scanner
    /// would flag its own explanatory documentation as a violation. A real violation hidden behind
    /// a trailing same-line comment (real code first, <c>//</c> comment appended after) is still
    /// caught, since only lines whose trimmed text starts with <c>//</c> are skipped in full.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class WireProtocol_Heartbeat_AOT_Tests
    {
        // =========================================================================================
        // AC-HB-1: HeartbeatMessage is envelope-only, exactly 10 bytes, no body beyond the envelope.
        // =========================================================================================

        [Test]
        public void HeartbeatMessage_WireSize_IsExactlyTenBytes()
        {
            // Assert — pins the wire-size invariant so a future edit cannot silently drift it.
            Assert.AreEqual(10, HeartbeatMessage.WireSize, "HeartbeatMessage wire size must be exactly 10 bytes (envelope only) per CR-NET-7.10.");
            Assert.AreEqual(ServerMessageEnvelope.WireSize, HeartbeatMessage.WireSize, "HeartbeatMessage.WireSize must equal ServerMessageEnvelope.WireSize — no body fields.");
        }

        [Test]
        public void HeartbeatMessage_MessageTypeId_IsProvisionalValueDocumentedInRemarks()
        {
            // Assert — pins the provisional MessageTypeID so a future edit cannot silently drift it
            // without this test failing (matches PriorityPathQueue<T>.PRIORITY_PATH_CAP's pin precedent).
            Assert.AreEqual(0x0210, HeartbeatMessage.MessageTypeId, "HeartbeatMessage.MessageTypeId must remain 0x0210 — see type-level remarks for provisionality.");
        }

        [Test]
        public void CreateEnvelope_WriteThenTryRead_RoundTripsAsExactlyTenBytesWithCorrectMessageTypeId()
        {
            // Arrange
            ServerMessageEnvelope envelope = HeartbeatMessage.CreateEnvelope(sequenceNumber: 12u, serverTickNumber: 500u);
            Span<byte> buffer = new byte[HeartbeatMessage.WireSize];

            // Act
            MessageEnvelopeCodec.Write(buffer, in envelope);
            bool ok = MessageEnvelopeCodec.TryRead(buffer, out ServerMessageEnvelope decoded);

            // Assert — exact 10-byte round trip, no body bytes involved.
            Assert.AreEqual(10, buffer.Length, "The buffer used to carry a HeartbeatMessage must be exactly 10 bytes — no body.");
            Assert.IsTrue(ok, "TryRead must succeed on a correctly-sized 10-byte buffer.");
            Assert.AreEqual(envelope, decoded, "Decoded envelope must equal the original.");
            Assert.AreEqual(HeartbeatMessage.MessageTypeId, decoded.MessageTypeId, "Decoded MessageTypeId must identify this as a HeartbeatMessage.");
            Assert.IsTrue(HeartbeatMessage.IsHeartbeatEnvelope(in decoded), "IsHeartbeatEnvelope must recognize the round-tripped envelope.");
        }

        [Test]
        public void CreateEnvelope_WriteToExactlyTenByteBuffer_DoesNotThrow_ProvingNoBodyBytesAreWritten()
        {
            // Arrange — a buffer sized to exactly the envelope, with no room for any body byte.
            ServerMessageEnvelope envelope = HeartbeatMessage.CreateEnvelope(sequenceNumber: 1u, serverTickNumber: 1u);
            byte[] buffer = new byte[HeartbeatMessage.WireSize];

            // Act & Assert — if HeartbeatMessage ever grew a body, this write would need a larger
            // buffer and would throw; succeeding here proves no body bytes are written beyond the
            // 10-byte envelope (AC-HB-1).
            Assert.DoesNotThrow(() => MessageEnvelopeCodec.Write(buffer, in envelope),
                "Writing a HeartbeatMessage's envelope must fit in exactly 10 bytes with no body.");
        }

        [Test]
        public void HeartbeatMessage_TwoIndependentlyConstructedInstances_AreEqualAndHaveSameHashCode()
        {
            // Arrange — cheap future-proofing (found during code review): pins that two independently
            // constructed HeartbeatMessage instances are Equals and hash identically. Trivial today
            // (the type has zero fields — every instance is definitionally equivalent), but this
            // struct is explicitly documented as a template future message types may be copy-pasted
            // from, so a regression that added a field without updating Equals/GetHashCode would be
            // silently missed without this pin.
            var a = default(HeartbeatMessage);
            var b = new HeartbeatMessage();

            // Assert
            Assert.IsTrue(a.Equals(b), "Two HeartbeatMessage instances must be Equals.");
            Assert.IsTrue(a == b, "Two HeartbeatMessage instances must compare equal via ==.");
            Assert.IsFalse(a != b, "Two HeartbeatMessage instances must not compare unequal via !=.");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Two equal HeartbeatMessage instances must have matching hash codes.");
        }

        [Test]
        public void IsHeartbeatEnvelope_DifferentMessageTypeId_ReturnsFalse()
        {
            // Arrange — a DamageEvent's MessageTypeId, not a heartbeat.
            var otherEnvelope = new ServerMessageEnvelope(DamageEvent.MessageTypeId, sequenceNumber: 1u, serverTickNumber: 1u);

            // Act & Assert
            Assert.IsFalse(HeartbeatMessage.IsHeartbeatEnvelope(in otherEnvelope), "IsHeartbeatEnvelope must return false for a non-heartbeat MessageTypeId.");
        }

        [Test]
        public void MessageEnvelopeCodec_TryReadHeartbeatEnvelope_TooShortSource_ReturnsFalse()
        {
            // Arrange — one byte short of the 10-byte wire size (reuses the existing envelope codec's
            // documented too-short behavior; HeartbeatMessage introduces no new decode path).
            ReadOnlySpan<byte> tooShort = new byte[9];

            // Act
            bool ok = MessageEnvelopeCodec.TryRead(tooShort, out ServerMessageEnvelope decoded);

            // Assert
            Assert.IsFalse(ok, "TryRead must return false for a source shorter than HeartbeatMessage.WireSize.");
        }

        // =========================================================================================
        // AC-NC-38: skip-on-activity heartbeat scheduling (HeartbeatActivityTracker).
        // =========================================================================================

        [Test]
        public void HEARTBEAT_INTERVAL_SECONDS_IsThreePerCrNet710()
        {
            // Assert — pins the GDD tuning-knob value so a future edit cannot silently drift it.
            Assert.AreEqual(3, HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS, "HEARTBEAT_INTERVAL_SECONDS must be 3 per CR-NET-7.10's tuning knob default.");
        }

        [Test]
        public void IsHeartbeatDue_NotifySkillUsedSentThenSilence_SkipsThenBecomesDueAtExactIntervalAndNotAgainUntilNextInterval()
        {
            // Arrange — CR-NET-7.10 / AC-NC-38 scenario, tick-count-only per this story's scope note.
            // intervalTicks stands in for HEARTBEAT_INTERVAL_SECONDS * a future TICK_RATE_HZ; the
            // concrete value 60 here is illustrative only (this story does not depend on TICK_RATE_HZ
            // existing — see HeartbeatActivityTracker's class remarks).
            var tracker = new HeartbeatActivityTracker();
            const uint tickOfSkillUsed = 100u;
            const uint intervalTicks = 60u;
            tracker.RecordOutboundPacket(tickOfSkillUsed);

            // Act & Assert — tick T completes: no heartbeat is due for T itself; the RPC already reset the counter.
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: tickOfSkillUsed, intervalTicks: intervalTicks), "No heartbeat is due for the same tick the RPC was sent on.");

            // Act & Assert — one tick before the full interval elapses: still not due.
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: tickOfSkillUsed + intervalTicks - 1, intervalTicks: intervalTicks), "Heartbeat must not be due one tick before the interval elapses.");

            // Act & Assert — exactly one full interval of silence: due (boundary-inclusive, per IsTickExpired's documented equality-is-expired semantics).
            uint dueTick = tickOfSkillUsed + intervalTicks;
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: dueTick, intervalTicks: intervalTicks), "Heartbeat must become due at exactly currentTick == lastOutboundPacketTick + intervalTicks.");

            // Act — the heartbeat itself is "sent": record it exactly like any other outbound packet.
            tracker.RecordOutboundPacket(dueTick);

            // Assert — a second heartbeat is not due immediately after the first was sent, nor before another full interval elapses.
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: dueTick, intervalTicks: intervalTicks), "A second heartbeat must not be due immediately after the first was sent.");
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: dueTick + intervalTicks - 1, intervalTicks: intervalTicks), "A second heartbeat must not be due before another full interval elapses.");

            // Assert — due again only after a full second interval of silence.
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: dueTick + intervalTicks, intervalTicks: intervalTicks), "A second heartbeat must become due after exactly another full interval of silence.");
        }

        [Test]
        public void IsHeartbeatDue_NeverRecordedAnyPacket_TreatsTickZeroAsImplicitLastOutbound()
        {
            // Arrange — documents the class's stated default-state judgment call (see class remarks).
            var tracker = new HeartbeatActivityTracker();
            const uint intervalTicks = 60u;

            // Act & Assert
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: 0u, intervalTicks: intervalTicks), "Before intervalTicks have elapsed since tick 0, a never-recorded tracker must not report a heartbeat due.");
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: intervalTicks - 1, intervalTicks: intervalTicks), "One tick before intervalTicks, a never-recorded tracker must still not report a heartbeat due.");
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: intervalTicks, intervalTicks: intervalTicks), "At exactly intervalTicks, a never-recorded tracker must report a heartbeat due.");
        }

        [Test]
        public void IsHeartbeatDue_LastOutboundPacketTickNearUintMaxValue_WrapsSafely()
        {
            // Arrange — boundary coverage near uint.MaxValue, reusing StaleDiscardComparer's own
            // wraparound guarantee (proactive coverage this project's reviewers have consistently
            // asked for in every prior story).
            var tracker = new HeartbeatActivityTracker();
            const uint intervalTicks = 10u;
            const uint lastPacketTick = uint.MaxValue - 5; // expiryTick = lastPacketTick + 10 wraps to 4.
            tracker.RecordOutboundPacket(lastPacketTick);

            // Act & Assert
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: uint.MaxValue, intervalTicks: intervalTicks), "Just-before-wraparound tick must not yet report the heartbeat due.");
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: 3u, intervalTicks: intervalTicks), "One tick before the wrapped expiry boundary must not report the heartbeat due.");
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: 4u, intervalTicks: intervalTicks), "Exactly at the wrapped expiry boundary (4) must report the heartbeat due.");
        }

        [Test]
        public void RecordOutboundPacket_CalledMultipleTimes_OnlyMostRecentTickMatters()
        {
            // Arrange — proactive coverage: an older RecordOutboundPacket call must not linger and
            // incorrectly suppress a later due-check once a newer tick has been recorded.
            var tracker = new HeartbeatActivityTracker();
            const uint intervalTicks = 20u;

            // Act
            tracker.RecordOutboundPacket(10u);
            tracker.RecordOutboundPacket(50u); // supersedes the tick=10 record entirely

            // Assert — due-ness is computed relative to tick 50, not tick 10.
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: 69u, intervalTicks: intervalTicks), "Due-ness must be computed relative to the most recently recorded outbound tick.");
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: 70u, intervalTicks: intervalTicks), "Due-ness must become true at exactly the most-recent-tick + intervalTicks.");
        }

        [Test]
        public void IsHeartbeatDue_IntervalTicksZero_AlwaysReportsDueFromLastRecordedTickOnward()
        {
            // Arrange — pins the degenerate intervalTicks=0 behavior (found during code review):
            // expiryTick collapses to exactly _lastOutboundPacketTick, so IsHeartbeatDue reports
            // true at and after the last recorded tick, on every subsequent check. This is a legal
            // (if degenerate) caller input — e.g. a future integration bug computing intervalTicks
            // as 0 — and must behave deterministically rather than being merely "probably fine."
            var tracker = new HeartbeatActivityTracker();
            tracker.RecordOutboundPacket(100u);

            // Assert
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: 100u, intervalTicks: 0u), "With intervalTicks=0, a heartbeat must be due at exactly the last recorded tick.");
            Assert.IsTrue(tracker.IsHeartbeatDue(currentTick: 101u, intervalTicks: 0u), "With intervalTicks=0, a heartbeat must remain due on every tick after the last recorded tick.");
            Assert.IsFalse(tracker.IsHeartbeatDue(currentTick: 99u, intervalTicks: 0u), "With intervalTicks=0, a heartbeat must not be due before the last recorded tick.");
        }

        // =========================================================================================
        // AC-AOT-1: lightweight source-scanning static analysis over src/Foundation/Networking/.
        // =========================================================================================

        [Test]
        public void AotGuardScanner_TypeofTGenericDispatch_IsFlagged()
        {
            // Arrange — deliberate CR-NET-7.8 violation fixture (self-test proving the scanner is not a no-op).
            const string fixtureSource =
@"namespace IronGrind.Networking.TestFixtures
{
    internal static class BadGenericSerializer
    {
        internal static void Serialize<T>(T value) where T : struct
        {
            var type = typeof(T); // deliberate CR-NET-7.8 violation
        }
    }
}";
            // Act
            List<AotViolation> violations = AotGuardScanner.ScanSourceText("Fixture_TypeofT.cs", fixtureSource);

            // Assert
            Assert.IsTrue(HasViolation(violations, "TypeofGenericDispatch"),
                "The scanner must flag a typeof(T) generic-dispatch pattern — proves this check is not a no-op.");
        }

        [Test]
        public void AotGuardScanner_LambdaCaptureEventRegistration_IsFlagged()
        {
            // Arrange — deliberate CR-NET-7.8 violation fixture (self-test proving the scanner is not a no-op).
            const string fixtureSource =
@"namespace IronGrind.Networking.TestFixtures
{
    internal static class BadHandlerRegistration
    {
        internal static void Register()
        {
            int captured = 42;
            SomeStaticEvent.Changed += x => { UnityEngine.Debug.Log(captured + x); }; // deliberate violation
        }
    }
}";
            // Act
            List<AotViolation> violations = AotGuardScanner.ScanSourceText("Fixture_LambdaCapture.cs", fixtureSource);

            // Assert
            Assert.IsTrue(HasViolation(violations, "LambdaCaptureHandlerRegistration"),
                "The scanner must flag a '+=' lambda-shaped event/delegate registration — proves this check is not a no-op.");
        }

        [Test]
        public void AotGuardScanner_BinaryFormatterAndJsonUtilityUsage_AreFlagged()
        {
            // Arrange — deliberate CR-NET-7.8 violation fixture.
            const string fixtureSource =
@"namespace IronGrind.Networking.TestFixtures
{
    internal static class BadSerializer
    {
        internal static void SerializeBad(object payload)
        {
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            string json = UnityEngine.JsonUtility.ToJson(payload);
        }
    }
}";
            // Act
            List<AotViolation> violations = AotGuardScanner.ScanSourceText("Fixture_ForbiddenSerializers.cs", fixtureSource);

            // Assert
            Assert.IsTrue(HasViolation(violations, "BinaryFormatter"), "The scanner must flag BinaryFormatter usage.");
            Assert.IsTrue(HasViolation(violations, "JsonUtility"), "The scanner must flag JsonUtility usage.");
        }

        [Test]
        public void AotGuardScanner_ReflectionEmitAndEnumIsDefinedUsage_AreFlagged()
        {
            // Arrange — bonus checks from the broader CR-NET-7.8 checklist (not required by AC-AOT-1's
            // literal three-part text, but cheap and zero-false-positive as substring matches).
            const string fixtureSource =
@"namespace IronGrind.Networking.TestFixtures
{
    internal static class BadReflectionUsage
    {
        internal static void EmitSomething()
        {
            var builder = System.Reflection.Emit.DynamicMethod;
        }

        internal static bool ValidateBad(System.Enum value)
        {
            return System.Enum.IsDefined(value.GetType(), value);
        }
    }
}";
            // Act
            List<AotViolation> violations = AotGuardScanner.ScanSourceText("Fixture_ReflectionEnum.cs", fixtureSource);

            // Assert
            Assert.IsTrue(HasViolation(violations, "ReflectionEmit"), "The scanner must flag System.Reflection.Emit usage.");
            Assert.IsTrue(HasViolation(violations, "EnumIsDefined"), "The scanner must flag an actual Enum.IsDefined(...) call site.");
        }

        [Test]
        public void AotGuardScanner_CleanFixtureWithDocCommentEnumIsDefinedReferenceAndLegitimatePatterns_ProducesZeroViolations()
        {
            // Arrange — proves the scanner does NOT false-positive on: (1) a doc-comment reference to
            // System.Enum.IsDefined explaining why it is forbidden (this codebase's actual convention
            // in DisconnectType.cs/DamageType.cs/WireEnumCodec.cs), (2) an ordinary non-generic-parameter
            // typeof() call, and (3) a named-method delegate registration (not a lambda).
            const string fixtureSource =
@"namespace IronGrind.Networking.TestFixtures
{
    /// <summary>
    /// Never uses <see cref=""System.Enum.IsDefined(System.Type, object)""/> — forbidden on IL2CPP.
    /// </summary>
    internal static class GoodCodec
    {
        internal static void Decode(byte rawByte)
        {
            System.Type t = typeof(int); // legitimate, non-generic-parameter typeof
        }

        internal static void RegisterHandler()
        {
            SomeStaticEvent.Changed += OnChanged; // named method delegate, not a lambda
        }

        private static void OnChanged(int x) { }
    }
}";
            // Act
            List<AotViolation> violations = AotGuardScanner.ScanSourceText("Fixture_Clean.cs", fixtureSource);

            // Assert
            Assert.AreEqual(0, violations.Count, "Clean fixture must produce zero violations: " + DescribeViolations(violations));
        }

        [Test]
        public void AotGuardScanner_DirectoryWithNoCsFiles_ReturnsEmptyList()
        {
            // Arrange — closes the documented-by-inspection-only assumption (found during code
            // review) that Directory.GetFiles returning zero matches doesn't throw and produces an
            // empty, not null, violations list. Use a fresh temp directory guaranteed to contain no
            // .cs files, cleaned up in the same test (no external state left behind).
            string tempDir = Path.Combine(Path.GetTempPath(), "IronGrind_AotScanner_EmptyDirTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // Act
                List<AotViolation> violations = AotGuardScanner.ScanDirectory(tempDir);

                // Assert
                Assert.IsNotNull(violations, "ScanDirectory must never return null.");
                Assert.AreEqual(0, violations.Count, "A directory with no .cs files must produce zero violations.");
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Test]
        public void AotGuardScanner_RealNetworkingSourceTree_ProducesZeroViolations()
        {
            // Arrange — the actual AC-AOT-1 assertion: scan the real production source tree.
            string sourceDir = ResolveNetworkingSourceDirectory();
            if (!Directory.Exists(sourceDir))
            {
                Assert.Fail($"Expected networking source directory not found: {sourceDir}. " +
                    "This test resolves src/Foundation/Networking as a sibling of Assets/ (see Packages/manifest.json's " +
                    "\"com.irongrind.src\": \"file:../src\" package reference), relative to Application.dataPath.");
                return;
            }

            // Act
            List<AotViolation> violations = AotGuardScanner.ScanDirectory(sourceDir);

            // Assert
            Assert.AreEqual(0, violations.Count,
                "AC-AOT-1: src/Foundation/Networking/ must contain zero CR-NET-7.8 violations. Found: " + DescribeViolations(violations));
        }

        // -----------------------------------------------------------------------
        // Test helpers.
        // -----------------------------------------------------------------------

        private static bool HasViolation(List<AotViolation> violations, string ruleName)
        {
            for (int i = 0; i < violations.Count; i++)
            {
                if (violations[i].RuleName == ruleName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeViolations(List<AotViolation> violations)
        {
            if (violations.Count == 0)
            {
                return "(none)";
            }

            var lines = new string[violations.Count];
            for (int i = 0; i < violations.Count; i++)
            {
                lines[i] = violations[i].ToString();
            }

            return string.Join("; ", lines);
        }

        /// <summary>
        /// Resolves the absolute path to <c>src/Foundation/Networking</c>. <c>src/</c> is a sibling
        /// Unity local package to <c>Assets/</c> at the project root (see
        /// <c>Packages/manifest.json</c>'s <c>"com.irongrind.src": "file:../src"</c> reference,
        /// relative to the <c>Packages/</c> folder) — so the project root is one level above
        /// <see cref="Application.dataPath"/>, and <c>src/</c> sits alongside <c>Assets/</c>.
        /// </summary>
        private static string ResolveNetworkingSourceDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, "src", "Foundation", "Networking"));
        }

        // =========================================================================================
        // AotGuardScanner — the lightweight, line-based AC-AOT-1 static-analysis scanner. See the
        // enclosing fixture's remarks for full scope and known imprecision.
        // =========================================================================================

        private static class AotGuardScanner
        {
            private static readonly Regex TypeofGenericDispatchPattern =
                new Regex(@"typeof\s*\(\s*T([A-Z0-9_]\w*)?\s*\)", RegexOptions.Compiled);

            private static readonly Regex LambdaCaptureHandlerRegistrationPattern =
                new Regex(@"\+=\s*[^;]*=>", RegexOptions.Compiled);

            private static readonly Regex BinaryFormatterPattern = new Regex(@"\bBinaryFormatter\b", RegexOptions.Compiled);
            private static readonly Regex JsonUtilityPattern = new Regex(@"\bJsonUtility\b", RegexOptions.Compiled);
            private static readonly Regex ReflectionEmitPattern = new Regex(@"Reflection\.Emit\b", RegexOptions.Compiled);
            private static readonly Regex EnumIsDefinedPattern = new Regex(@"Enum\.IsDefined\s*\(", RegexOptions.Compiled);

            /// <summary>
            /// Scans a single file's already-read source text for CR-NET-7.8 forbidden patterns,
            /// skipping full-line comments (see enclosing fixture's remarks). Never throws on
            /// malformed input — worst case is zero matches.
            /// </summary>
            internal static List<AotViolation> ScanSourceText(string filePath, string sourceText)
            {
                var violations = new List<AotViolation>();
                string[] lines = sourceText.Replace("\r\n", "\n").Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();

                    // Full-line comments (including /// doc comments) are never scanned — required,
                    // not merely convenient (see enclosing fixture's remarks).
                    if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int lineNumber = i + 1;

                    if (TypeofGenericDispatchPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "TypeofGenericDispatch", line));
                    }

                    if (LambdaCaptureHandlerRegistrationPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "LambdaCaptureHandlerRegistration", line));
                    }

                    if (BinaryFormatterPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "BinaryFormatter", line));
                    }

                    if (JsonUtilityPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "JsonUtility", line));
                    }

                    if (ReflectionEmitPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "ReflectionEmit", line));
                    }

                    if (EnumIsDefinedPattern.IsMatch(line))
                    {
                        violations.Add(new AotViolation(filePath, lineNumber, "EnumIsDefined", line));
                    }
                }

                return violations;
            }

            /// <summary>Scans every <c>*.cs</c> file under <paramref name="rootDir"/>, recursively, in deterministic (sorted) order.</summary>
            internal static List<AotViolation> ScanDirectory(string rootDir)
            {
                var violations = new List<AotViolation>();
                string[] files = Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);

                for (int i = 0; i < files.Length; i++)
                {
                    string text = File.ReadAllText(files[i]);
                    violations.AddRange(ScanSourceText(files[i], text));
                }

                return violations;
            }
        }

        private readonly struct AotViolation
        {
            internal readonly string FilePath;
            internal readonly int LineNumber;
            internal readonly string RuleName;
            internal readonly string LineText;

            internal AotViolation(string filePath, int lineNumber, string ruleName, string lineText)
            {
                FilePath = filePath;
                LineNumber = lineNumber;
                RuleName = ruleName;
                LineText = lineText;
            }

            public override string ToString() => $"{FilePath}:{LineNumber} [{RuleName}] {LineText.Trim()}";
        }
    }
}
