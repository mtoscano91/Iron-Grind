using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 025 — the unified MCR-2/CCR-3 message-routing
    /// table (<see cref="MessageRoutingRegistry"/>), the EC-MCR-1 unclassified-message fallback, the
    /// MCR-3 multi-pillar resolution rule, the CCR-1 shared-sequence-counter proof, and the CCR-2
    /// naming-disambiguation check. Covers all six blocking ACs: AC-MCR-03, AC-MCR-04, AC-MCR-06,
    /// AC-CCR-01, AC-CCR-02, AC-CCR-09.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AC-MCR-04/AC-CCR-09 and AC-CCR-01 are implemented as in-file C# scanners, not new bash
    /// scripts/CI jobs</b> — this follows Story 008's <c>WireProtocol_Heartbeat_AOT_tests.cs</c>
    /// precedent for AC-AOT-1 (also a "(CI)"-typed static-analysis AC) exactly: a private nested
    /// scanner class, a self-test fixture proving it is not a no-op, and a real-tree scan asserting
    /// zero violations against the actual repository state. This differs from Story 002's
    /// <c>tools/ci/check-test-harness-guards.sh</c> precedent (a standalone bash script wired as a
    /// separate <c>tests.yml</c> job) — that precedent exists for a check needed <i>before</i> a
    /// Unity/IL2CPP build even runs; these three checks are pure text search / reflection over
    /// already-compiled C# constants, which the existing blocking <c>test</c> job (game-ci/unity-test-runner,
    /// EditMode) already gates on. No new <c>.github/workflows/tests.yml</c> entries were added for
    /// this story — confirmed with the technical-director.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class MessageRouting_CriticalityChannelTable_Tests
    {
        private NetworkTestObserver _observer;

        [SetUp]
        public void SetUp()
        {
            _observer = new NetworkTestObserver();
        }

        // =========================================================================================
        // AC-MCR-03: unregistered MessageTypeID — debug-fatal / release-log-and-route-to-R-U.
        // =========================================================================================

        [Test]
        public void ValidateAndRoute_RegisteredMessageType_DebugBuild_ReturnsClassifiedResult_NeverThrowsNeverLogs()
        {
            // Act
            MessageRoutingResult result = MessageRoutingRegistry.ValidateAndRoute(
                DamageEvent.MessageTypeId, isDevelopmentBuild: true, _observer);

            // Assert
            Assert.IsTrue(result.WasClassified, "A registered MessageTypeId must be classified, regardless of build type.");
            Assert.AreEqual(NetworkChannel.ReliableUnordered, result.RoutedChannel);
            Assert.AreEqual(0, _observer.UnclassifiedMessageTypeLoggedCalls.Count, "A registered MessageTypeId must never fire the unclassified-anomaly callback.");
        }

        [Test]
        public void ValidateAndRoute_RegisteredMessageType_ReleaseBuild_ReturnsClassifiedResult_NeverThrowsNeverLogs()
        {
            // Act
            MessageRoutingResult result = MessageRoutingRegistry.ValidateAndRoute(
                GoldSyncEvent.MessageTypeId, isDevelopmentBuild: false, _observer);

            // Assert
            Assert.IsTrue(result.WasClassified);
            Assert.AreEqual(0, _observer.UnclassifiedMessageTypeLoggedCalls.Count);
        }

        [Test]
        public void ValidateAndRoute_UnregisteredMessageType_DebugBuild_ThrowsPendingSchemaDispatchException_WithCorrectMessageTypeId()
        {
            // Arrange
            const ushort unregisteredId = 0xBEEF;

            // Act & Assert
            PendingSchemaDispatchException ex = Assert.Throws<PendingSchemaDispatchException>(() =>
                MessageRoutingRegistry.ValidateAndRoute(unregisteredId, isDevelopmentBuild: true, _observer));

            Assert.AreEqual(unregisteredId, ex.MessageTypeId, "The exception must carry the unregistered MessageTypeId (EC-MCR-1/AC-MCR-03).");
            Assert.AreEqual(0, _observer.UnclassifiedMessageTypeLoggedCalls.Count,
                "The debug-build fatal path must not also fire the release-build anomaly callback — the two paths are mutually exclusive.");
        }

        [Test]
        public void ValidateAndRoute_UnregisteredMessageType_ReleaseBuild_RoutesToReliableUnordered_AndLogsAnomalyExactlyOnce()
        {
            // Arrange
            const ushort unregisteredId = 0xBEEF;

            // Act
            MessageRoutingResult result = MessageRoutingRegistry.ValidateAndRoute(unregisteredId, isDevelopmentBuild: false, _observer);

            // Assert — EC-MCR-1: not dropped, routed to R-U, anomaly logged with the MessageTypeId.
            Assert.IsFalse(result.WasClassified);
            Assert.AreEqual(unregisteredId, result.MessageTypeId);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, result.RoutedChannel,
                "EC-MCR-1: unclassified messages must route to R-U as the safe default — never U-U, never R-OD.");
            Assert.AreEqual(1, _observer.UnclassifiedMessageTypeLoggedCalls.Count, "Exactly one anomaly must be logged per unclassified dispatch.");
            Assert.AreEqual(unregisteredId, _observer.UnclassifiedMessageTypeLoggedCalls[0]);
        }

        [Test]
        public void ValidateAndRoute_UnregisteredMessageType_ReleaseBuild_NullObserver_DoesNotThrow()
        {
            // Assert — the observer parameter is optional; production release-build behavior must not
            // depend on a test harness being present.
            Assert.DoesNotThrow(() => MessageRoutingRegistry.ValidateAndRoute(0xBEEF, isDevelopmentBuild: false, observer: null));
        }

        // =========================================================================================
        // AC-MCR-04 / AC-CCR-09: completeness — every real WireProtocol MessageTypeId has exactly
        // one registry row, and vice versa. Implemented as one shared check per the story's own
        // Implementation Notes ("these two ACs become one consistency check in practice").
        // =========================================================================================

        [Test]
        public void WireProtocolMessageTypeIdScanner_SelfTest_FindsConstFieldAndIgnoresNonConstOrAbsentField()
        {
            // Arrange — proves the reflection scanner is not a no-op before trusting it against the
            // real assembly (same idiom as AotGuardScanner's fixture self-tests, Story 008).
            List<(string TypeName, ushort MessageTypeId)> found =
                WireProtocolMessageTypeIdScanner.ScanAssembly(typeof(MessageRouting_CriticalityChannelTable_Tests).Assembly);

            // Assert
            Assert.IsTrue(HasEntry(found, nameof(ScannerFixtures.FixtureWithConstMessageTypeId), 0xABCD),
                "The scanner must find a public const ushort MessageTypeId field.");
            Assert.IsFalse(HasEntry(found, nameof(ScannerFixtures.FixtureWithNonConstMessageTypeId), null),
                "The scanner must ignore a non-const (static readonly) MessageTypeId field — const fields only.");
            Assert.IsFalse(HasEntry(found, nameof(ScannerFixtures.FixtureWithNoMessageTypeIdField), null),
                "The scanner must ignore a type with no MessageTypeId field at all.");
        }

        [Test]
        public void Registry_ContainsExactlyOneRowPerRealWireProtocolMessageTypeId_AC_MCR_04_AC_CCR_09()
        {
            // Arrange — scan the real Foundation assembly (where DamageEvent etc. live) for every
            // public const ushort MessageTypeId field.
            List<(string TypeName, ushort MessageTypeId)> discovered =
                WireProtocolMessageTypeIdScanner.ScanAssembly(typeof(DamageEvent).Assembly);

            Assert.GreaterOrEqual(discovered.Count, 5,
                "Expected to discover at least the 5 known WireProtocol MessageTypeId consts (HeartbeatMessage, DamageEvent, CycleTimerBroadcast, EntityPositionUpdate, GoldSyncEvent).");

            // Act & Assert — AC-MCR-04: every discovered MessageTypeId has exactly one registry row.
            var seenIds = new HashSet<ushort>();
            foreach ((string typeName, ushort messageTypeId) in discovered)
            {
                Assert.IsTrue(seenIds.Add(messageTypeId),
                    $"Duplicate MessageTypeId 0x{messageTypeId:X4} declared by more than one WireProtocol type ('{typeName}' collides with an earlier type).");
                Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(messageTypeId, out MessageRoutingEntry entry),
                    $"AC-MCR-04: '{typeName}' declares MessageTypeId 0x{messageTypeId:X4} but MessageRoutingRegistry has no row for it.");
                Assert.AreEqual(typeName, entry.MessageName,
                    $"Registry row for 0x{messageTypeId:X4} is named '{entry.MessageName}' but the discovered type is named '{typeName}'.");
            }

            // Act & Assert — AC-CCR-09 (reverse direction): every registry row corresponds to a real,
            // discovered WireProtocol MessageTypeId — no orphaned/fictitious rows.
            foreach (MessageRoutingEntry entry in MessageRoutingRegistry.AllEntries)
            {
                Assert.IsTrue(seenIds.Contains(entry.MessageTypeId),
                    $"Registry row '{entry.MessageName}' (0x{entry.MessageTypeId:X4}) does not correspond to any real WireProtocol MessageTypeId constant.");
            }

            // Assert — the two sets are exactly the same size (one-to-one, not just subset-of).
            Assert.AreEqual(discovered.Count, MessageRoutingRegistry.AllEntries.Count,
                "The registry must contain exactly one row per discovered WireProtocol MessageTypeId — no more, no fewer.");
        }

        [Test]
        public void Registry_HeartbeatMessageRow_MatchesDocumentedMcr2Ccr3Values()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(HeartbeatMessage.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(DesignPillar.Infrastructure, entry.Pillars);
            Assert.AreEqual(NetworkChannel.Unreliable, entry.Channel);
            Assert.AreEqual(MessageDirection.ClientToServer, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.Standalone, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void Registry_DamageEventRow_MatchesDocumentedMcr2Ccr3Values()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(DamageEvent.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(DesignPillar.RhythmMastery, entry.Pillars);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToAllZoneClients, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.ReliableUnorderedBatch, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void Registry_CycleTimerBroadcastRow_MatchesDocumentedMcr2Ccr3Values()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(CycleTimerBroadcast.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(DesignPillar.RhythmMastery, entry.Pillars);
            Assert.AreEqual(NetworkChannel.Unreliable, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToAllZoneClients, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.CycleBroadcastPacket, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void Registry_EntityPositionUpdateRow_MatchesDocumentedMcr2Ccr3Values()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(EntityPositionUpdate.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(DesignPillar.SocialSignals, entry.Pillars);
            Assert.AreEqual(NetworkChannel.Unreliable, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToAllZoneClients, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.PositionPacket, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void Registry_GoldSyncEventRow_MatchesDocumentedMcr2Ccr3Values_AndIsFlaggedAsMcr3Exception()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(GoldSyncEvent.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(DesignPillar.EarnedPower, entry.Pillars);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToOwningClient, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.ReliableUnorderedBatch, entry.DeliveryContext);
            Assert.IsTrue(entry.IsMcr3Exception, "GoldSyncEvent must be flagged as an MCR-3 exception per AC-MCR-06's explicit exception list.");
        }

        // =========================================================================================
        // AC-MCR-06: MCR-3 multi-pillar resolution — highest guarantee wins, except the three named
        // exceptions. Non-GoldSyncEvent scenarios below use literal pillar-channel arrays standing in
        // for messages not yet implemented in this codebase (KillEvent, GroundItemSpawned,
        // PartyMemberHealthUpdate, LootBidUpdate) — no new registry rows are added for them, per this
        // story's Out-of-Scope boundary.
        // =========================================================================================

        [Test]
        public void ResolveMultiPillarChannel_KillEventShape_BothPillarsRequireReliableOrdered_ReturnsReliableOrdered()
        {
            // KillEvent: Pillar 1 (kill credit, R-OD) + Pillar 3 (social broadcast, R-OD) -- MCR-3: "both require it independently".
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { NetworkChannel.ReliableOrdered, NetworkChannel.ReliableOrdered }, isDocumentedException: false);

            Assert.AreEqual(NetworkChannel.ReliableOrdered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_GroundItemSpawnedShape_MixedGuarantees_ReturnsHighestGuarantee()
        {
            // GroundItemSpawned: Pillar 1 (permanent economic event, R-OD) + Pillar 3 (party social coordination, R-U alone) -- max = R-OD.
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { NetworkChannel.ReliableOrdered, NetworkChannel.ReliableUnordered }, isDocumentedException: false);

            Assert.AreEqual(NetworkChannel.ReliableOrdered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_EntityHealthUpdateShape_BothPillarsRequireReliableUnordered_ReturnsReliableUnordered()
        {
            // EntityHealthUpdate: Pillar 2 (tactical combat feedback, R-U) + Pillar 3 (party-visible health bar, R-U).
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { NetworkChannel.ReliableUnordered, NetworkChannel.ReliableUnordered }, isDocumentedException: false);

            Assert.AreEqual(NetworkChannel.ReliableUnordered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_GoldSyncEventRealRegistryRow_DocumentedException_StaysReliableUnordered()
        {
            // Arrange -- use the real registry row, not a literal fixture, for the one MCR-3 exception this story implements.
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(GoldSyncEvent.MessageTypeId, out MessageRoutingEntry entry));

            // Act
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { entry.Channel }, entry.IsMcr3Exception, entry.Channel);

            // Assert
            Assert.AreEqual(NetworkChannel.ReliableUnordered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_PartyMemberHealthUpdateShape_DocumentedExceptionOverridesNaiveEscalation()
        {
            // PartyMemberHealthUpdate: Pillar 1 (XP/death-sequencing) would naively imply R-OD, Pillar 3 (party HP bar) is R-U.
            // MCR-2's rationale documents this as an MCR-3 exception -- HP self-corrects; GhostPromotionEvent (R-OD) separately
            // guarantees the death-transition outcome. The exception channel stays R-U even though a naive max would pick R-OD.
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { NetworkChannel.ReliableOrdered, NetworkChannel.ReliableUnordered },
                isDocumentedException: true,
                documentedExceptionChannel: NetworkChannel.ReliableUnordered);

            Assert.AreEqual(NetworkChannel.ReliableUnordered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_LootBidUpdateShape_DocumentedException_StaysReliableUnordered()
        {
            NetworkChannel result = MessageRoutingRegistry.ResolveMultiPillarChannel(
                new[] { NetworkChannel.ReliableUnordered, NetworkChannel.ReliableUnordered },
                isDocumentedException: true,
                documentedExceptionChannel: NetworkChannel.ReliableUnordered);

            Assert.AreEqual(NetworkChannel.ReliableUnordered, result);
        }

        [Test]
        public void ResolveMultiPillarChannel_EmptyTaggedChannels_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MessageRoutingRegistry.ResolveMultiPillarChannel(Array.Empty<NetworkChannel>(), isDocumentedException: false));
        }

        [Test]
        public void ResolveMultiPillarChannel_NullTaggedChannels_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                MessageRoutingRegistry.ResolveMultiPillarChannel(null, isDocumentedException: false));
        }

        [Test]
        public void ResolveMultiPillarChannel_ExceptionWithoutOverrideChannel_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MessageRoutingRegistry.ResolveMultiPillarChannel(new[] { NetworkChannel.ReliableUnordered }, isDocumentedException: true));
        }

        // =========================================================================================
        // AC-CCR-01: CI text search for "server SessionHandshake" scoped to src/ and tests/ only.
        // =========================================================================================

        [Test]
        public void SessionHandshakeNamingScanner_ViolationFixture_IsFlagged()
        {
            // Arrange -- deliberate CCR-2 violation fixture (self-test proving the scanner is not a no-op).
            const string fixtureSource = "// TODO: the server SessionHandshake response carries initial state\nSomeCode();";

            // Act
            List<(string FilePath, int LineNumber, string LineText)> violations =
                SessionHandshakeNamingScanner.ScanSourceText("Fixture_Violation.cs", fixtureSource);

            // Assert
            Assert.AreEqual(1, violations.Count, "The scanner must flag the literal phrase 'server SessionHandshake'.");
        }

        [Test]
        public void SessionHandshakeNamingScanner_CleanFixtureWithOrdinarySessionHandshakeReferences_ProducesZeroViolations()
        {
            // Arrange -- legitimate CCR-2-compliant references must not be flagged.
            const string fixtureSource =
@"// SessionHandshake is C->S only; SessionReady is S->C (CCR-2 disambiguation).
public readonly struct SessionHandshakeData { }
// The server never sends a SessionHandshake -- only SessionReady and ZoneStateSnapshotFragment.";

            // Act
            List<(string FilePath, int LineNumber, string LineText)> violations =
                SessionHandshakeNamingScanner.ScanSourceText("Fixture_Clean.cs", fixtureSource);

            // Assert
            Assert.AreEqual(0, violations.Count, "Ordinary SessionHandshake/SessionReady references must not be flagged -- only the literal 'server SessionHandshake' phrase.");
        }

        [Test]
        public void SessionHandshakeNamingScanner_RealSrcAndTestsTrees_ProduceZeroViolations_AC_CCR_01()
        {
            // Arrange -- scoped to src/ and tests/ only, excluding design/gdd/ and production/epics/
            // (where the phrase legitimately appears solely as part of CCR-2's own rule definition
            // and this story's own text -- see story-025's corrected AC-CCR-01 scope). This test file
            // itself is additionally excluded from the tests/ scan: it necessarily declares the exact
            // literal phrase in ForbiddenPhrase and in its own fixture/assertion strings to define and
            // prove the check, which is the same "rule's own definition" situation carved out for the
            // GDD/story -- not a real CCR-2 violation. GetThisFilePath() captures the path via
            // [CallerFilePath] rather than a hardcoded string so the exclusion survives a file rename.
            string repoRoot = ResolveRepoRootDirectory();
            string srcDir = Path.Combine(repoRoot, "src");
            string testsDir = Path.Combine(repoRoot, "tests");
            string ownFilePath = Path.GetFullPath(GetThisFilePath());

            Assert.IsTrue(Directory.Exists(srcDir), $"Expected a src/ directory at {srcDir}.");
            Assert.IsTrue(Directory.Exists(testsDir), $"Expected a tests/ directory at {testsDir}.");

            // Act
            var violations = new List<(string FilePath, int LineNumber, string LineText)>();
            violations.AddRange(SessionHandshakeNamingScanner.ScanDirectory(srcDir, excludeFilePath: null, ".cs", ".md"));
            violations.AddRange(SessionHandshakeNamingScanner.ScanDirectory(testsDir, excludeFilePath: ownFilePath, ".cs", ".md"));

            // Assert
            Assert.AreEqual(0, violations.Count,
                "AC-CCR-01: 'server SessionHandshake' must not appear anywhere under src/ or tests/. Found: " + DescribeNamingViolations(violations));
        }

        /// <summary>Captures this source file's own absolute path at compile time, for the self-exclusion above.</summary>
        private static string GetThisFilePath([CallerFilePath] string path = "") => path;

        // =========================================================================================
        // AC-CCR-02: CCR-1 shared per-connection SequenceNumber counter proof.
        // =========================================================================================

        [Test]
        public void ConnectionSequenceCounter_HeartbeatThenRttProbeEcho_SecondSequenceIsFirstPlusOne_AC_CCR_02()
        {
            // Arrange -- no RttProbeEcho message class exists yet in this codebase (confirmed by
            // grep); this local constant stands in for its MessageTypeID purely to label the second
            // envelope below, per this story's documented structural-proof scope.
            const ushort fakeRttProbeEchoMessageTypeId = 0x0211;
            const uint tick = 100u;
            var counter = new ConnectionSequenceCounter();

            // Act -- both envelopes are issued from the SAME shared counter instance, exactly as a
            // real per-connection counter would be used for every message type that connection sends.
            ServerMessageEnvelope heartbeatEnvelope = HeartbeatMessage.CreateEnvelope(counter.IssueNext(), tick);
            var rttProbeEchoEnvelope = new ServerMessageEnvelope(fakeRttProbeEchoMessageTypeId, counter.IssueNext(), tick);

            // Assert -- CCR-1: RttProbeEcho.SequenceNumber == HeartbeatMessage.SequenceNumber + 1 (shared counter, not per-type).
            Assert.AreEqual(heartbeatEnvelope.SequenceNumber + 1, rttProbeEchoEnvelope.SequenceNumber);
        }

        [Test]
        public void ConnectionSequenceCounter_StartsAtOne()
        {
            var counter = new ConnectionSequenceCounter();
            Assert.AreEqual(1u, counter.IssueNext(), "CCR-1: SequenceNumber starts at 1; 0 is reserved as uninitialized.");
        }

        [Test]
        public void ConnectionSequenceCounter_ThirdMessageOfAnyType_ContinuesTheSameSharedSequence()
        {
            // Arrange -- proactive coverage: a third, fourth... message of any type must keep
            // advancing the one shared counter, not just the first-two-messages case AC-CCR-02 names.
            var counter = new ConnectionSequenceCounter();

            // Act
            uint first = counter.IssueNext();
            uint second = counter.IssueNext();
            uint third = counter.IssueNext();

            // Assert
            Assert.AreEqual(first + 1, second);
            Assert.AreEqual(second + 1, third);
        }

        [Test]
        public void ConnectionSequenceCounter_WrapsFromUintMaxValueToOne_NeverEmitsZero()
        {
            // Arrange -- test-only seeded constructor (internal, InternalsVisibleTo) avoids iterating
            // billions of calls to reach the real wraparound boundary.
            var counter = new ConnectionSequenceCounter(uint.MaxValue);

            // Act
            uint last = counter.IssueNext();
            uint afterWrap = counter.IssueNext();

            // Assert -- CCR-1: 0 must never appear in a valid message.
            Assert.AreEqual(uint.MaxValue, last);
            Assert.AreEqual(1u, afterWrap, "The counter must wrap from uint.MaxValue directly to 1, never emitting 0.");
        }

        // -----------------------------------------------------------------------
        // Test helpers.
        // -----------------------------------------------------------------------

        private static bool HasEntry(List<(string TypeName, ushort MessageTypeId)> found, string typeName, ushort? expectedId)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].TypeName == typeName && (!expectedId.HasValue || found[i].MessageTypeId == expectedId.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeNamingViolations(List<(string FilePath, int LineNumber, string LineText)> violations)
        {
            if (violations.Count == 0)
            {
                return "(none)";
            }

            var lines = new string[violations.Count];
            for (int i = 0; i < violations.Count; i++)
            {
                lines[i] = $"{violations[i].FilePath}:{violations[i].LineNumber} {violations[i].LineText.Trim()}";
            }

            return string.Join("; ", lines);
        }

        /// <summary>
        /// Resolves the absolute repository root. <c>src/</c> and <c>tests/</c> are sibling Unity
        /// local packages to <c>Assets/</c> at the project root (<c>Packages/manifest.json</c>'s
        /// <c>"com.irongrind.src": "file:../src"</c> / <c>"com.irongrind.tests": "file:../tests"</c>
        /// references) — same resolution precedent as
        /// <c>WireProtocol_Heartbeat_AOT_tests.cs</c>'s <c>ResolveNetworkingSourceDirectory()</c>.
        /// </summary>
        private static string ResolveRepoRootDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        // =========================================================================================
        // WireProtocolMessageTypeIdScanner — reflection scan over an assembly's types for a public
        // const ushort MessageTypeId field. Backs AC-MCR-04/AC-CCR-09.
        // =========================================================================================

        private static class WireProtocolMessageTypeIdScanner
        {
            /// <summary>
            /// Scans every type in <paramref name="assembly"/> (including nested types) for a
            /// declared <c>public const ushort MessageTypeId</c> field, returning the declaring
            /// type's name and the constant's value for each match, in deterministic (ordinal)
            /// order. A <c>static readonly</c> field of the same name and type is deliberately NOT
            /// matched (<c>FieldInfo.IsLiteral</c> is <see langword="false"/> for it) — only a true
            /// compile-time <c>const</c> counts as a registered wire <c>MessageTypeID</c>.
            /// </summary>
            internal static List<(string TypeName, ushort MessageTypeId)> ScanAssembly(Assembly assembly)
            {
                var results = new List<(string, ushort)>();
                Type[] types = assembly.GetTypes();
                Array.Sort(types, (a, b) => string.CompareOrdinal(a.FullName, b.FullName));

                for (int i = 0; i < types.Length; i++)
                {
                    FieldInfo field = types[i].GetField(
                        "MessageTypeId", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

                    if (field != null && field.IsLiteral && field.FieldType == typeof(ushort))
                    {
                        // Convert.ToUInt16 (not a direct unboxing cast) tolerates whichever exact
                        // boxed integral type the runtime's metadata reader returns for a `const
                        // ushort` field -- a direct `(ushort)` unboxing cast throws
                        // InvalidCastException unless the boxed type is precisely System.UInt16.
                        ushort value = Convert.ToUInt16(field.GetRawConstantValue());
                        results.Add((types[i].Name, value));
                    }
                }

                return results;
            }
        }

        /// <summary>Fixture types for <see cref="WireProtocolMessageTypeIdScanner"/>'s self-test.</summary>
        private static class ScannerFixtures
        {
            internal static class FixtureWithConstMessageTypeId
            {
                public const ushort MessageTypeId = 0xABCD;
            }

            internal static class FixtureWithNonConstMessageTypeId
            {
                // Deliberately NOT const -- must be ignored by the scanner (proves the IsLiteral guard matters).
                public static readonly ushort MessageTypeId = 0x1234;
            }

            internal static class FixtureWithNoMessageTypeIdField
            {
                public const int SomethingElse = 1;
            }
        }

        // =========================================================================================
        // SessionHandshakeNamingScanner — pure text search for the literal phrase
        // "server SessionHandshake" (CCR-2). Backs AC-CCR-01.
        // =========================================================================================

        private static class SessionHandshakeNamingScanner
        {
            private const string ForbiddenPhrase = "server SessionHandshake";

            /// <summary>Scans already-read source text line by line for the literal forbidden phrase.</summary>
            internal static List<(string FilePath, int LineNumber, string LineText)> ScanSourceText(string filePath, string sourceText)
            {
                var violations = new List<(string, int, string)>();
                string[] lines = sourceText.Replace("\r\n", "\n").Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(ForbiddenPhrase, StringComparison.Ordinal) >= 0)
                    {
                        violations.Add((filePath, i + 1, lines[i]));
                    }
                }

                return violations;
            }

            /// <summary>
            /// Scans every file under <paramref name="rootDir"/> matching any of <paramref name="extensions"/>,
            /// recursively, in deterministic order. <paramref name="excludeFilePath"/> (if not
            /// <see langword="null"/>) is skipped — used to exclude this scanner's own defining file,
            /// which necessarily contains the literal forbidden phrase to define/test the check itself.
            /// </summary>
            internal static List<(string FilePath, int LineNumber, string LineText)> ScanDirectory(
                string rootDir, string excludeFilePath, params string[] extensions)
            {
                var violations = new List<(string, int, string)>();

                for (int e = 0; e < extensions.Length; e++)
                {
                    string[] files = Directory.GetFiles(rootDir, "*" + extensions[e], SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.Ordinal);

                    for (int i = 0; i < files.Length; i++)
                    {
                        if (excludeFilePath != null && string.Equals(Path.GetFullPath(files[i]), excludeFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        violations.AddRange(ScanSourceText(files[i], File.ReadAllText(files[i])));
                    }
                }

                return violations;
            }
        }
    }
}
