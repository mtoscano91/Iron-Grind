# Networking Test Harness

> **Status**: Approved (lean re-review, 2026-05-14)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-29 (OQ-ZI-5: OnZoneGateOpened + GetOutboundMessageCount added to INetworkTestObserver)
> **Parent**: networking-core.md

## Overview

Networking Test Harness defines the injectable test interfaces required to validate the Networking Core's behavioral contracts without relying on "server debug output." Four interfaces are specified: `ITransportFaultInjector` (network condition simulation), `IServerCrashInjector` (controlled crash injection), `IZoneTestConfigurator` (zone instance configuration and state readback), and `INetworkTestObserver` (structured server-value capture at the server serialization and client transport boundaries). All four must be stripped from release builds and are unavailable at runtime outside test and development configurations.

## Player Fantasy

None. This document is pure test infrastructure with no player-facing content.

## Detailed Rules

### Interface Definitions (B-QA-3/B-QA-SYSTEMIC)

**ITransportFaultInjector**

Injects simulated transport conditions at the networking stack boundary, below game logic and above the physical transport. Allows tests to reproduce packet loss, reordering, delay, and fragmentation drops without real network degradation.

```csharp
// COMPILE ONLY in UNITY_INCLUDE_TESTS or DEVELOPMENT_BUILD — never in release.
public interface ITransportFaultInjector
{
    // Drop the next N outbound messages matching the given MessageTypeID.
    // Pass MessageTypeID.Any to drop the next N messages of any type.
    void DropNextOutbound(ushort messageTypeId, int count);

    // Delay the next N outbound messages matching the given MessageTypeID by delayMs milliseconds.
    void DelayNextOutbound(ushort messageTypeId, int count, int delayMs);

    // Reorder the next two messages matching the given MessageTypeID — deliver the second before the first.
    void ReorderNext(ushort messageTypeId);

    // Drop fragment index fragmentIndex of the next fragmented ZoneStateSnapshot transmission.
    // Fragment indices are 0-based. Matches ZoneStateSnapshotFragment.fragmentIndex wire type (uint16).
    // Pass ushort.MaxValue (65535) to drop the last fragment regardless of totalFragments count —
    // required when totalFragments is not known before the snapshot begins transmitting (dynamic per CR-NET-7.3).
    void DropSnapshotFragment(ushort fragmentIndex);

    // Override the outbound SequenceNumber counter for this connection to the given value.
    // Must be called before any message is emitted; if called mid-stream, takes effect atomically
    // at the next tick boundary. Used to test EC-NET-6 wraparound (wire-protocol AC-NC-36):
    // set to 4,294,967,293 to trigger wraparound after 3 messages (0xFFFFFFFD → E → F → 0 excluded).
    void SetSequenceNumber(uint value);

    // Reset all pending fault injections.
    void Reset();
}
```

*Supported ACs:* AC-NC-07 (GoldSyncEvent reordering: `ReorderNext`), AC-NC-30 (kill/damage cross-path race: `DelayNextOutbound`), AC-NC-35 (snapshot fragment loss: `DropSnapshotFragment`), wire-protocol AC-NC-36 (SequenceNumber wraparound: `SetSequenceNumber`).

---

**IServerCrashInjector**

Triggers a controlled, reproducible server crash at a specified step in a critical sequence. Used to verify that persistence and recovery behave correctly under crash conditions (AC-NC-16, AC-NC-34).

```csharp
// COMPILE ONLY in UNITY_INCLUDE_TESTS or DEVELOPMENT_BUILD — never in release.
public interface IServerCrashInjector
{
    // Register a crash to trigger at the specified step of a critical persistence sequence.
    // Covers three sequences: CR-NET-5 enhancement, CR-NET-6.5 TTL-expiry, CR-GH-10 ghost-cleanup.
    void RegisterCrashAt(CrashStep step);

    // Clear any registered crash injection.
    void ClearRegistered();
}

public enum CrashStep : byte
{
    // CR-NET-5 enhancement sequence
    AfterPersistenceWrite             = 0,  // outcome committed, before broadcast
    AfterOutcomeEmit                  = 1,  // outcome queued for transmission, before transport confirms
    AfterPersistenceWriteRespec       = 2,  // respec commit-before-broadcast (CR-NET-5.6)

    // CR-NET-6.5 session-TTL-expiry sequence
    AfterTTLExpiryPersistenceWrite    = 3,  // character state committed (step 3), before PlayerLeftZone (step 4)

    // CR-GH-10 ghost-cleanup sequence
    AfterGhostCleanupPersistenceWrite = 4,  // ghost write committed, before session slot release
}
```

*Supported ACs:* AC-NC-16 (crash after write, before broadcast — confirms durable outcome on reconnect), AC-NC-34 (same crash confirms `LastEnhancementRequestID` persistence and duplicate rejection).

---

**IZoneTestConfigurator**

Provides per-test control over zone instance configuration. Allows tests to inject known entry-point coordinates, capacity limits, and read zone state machine state without polling logs.

```csharp
// COMPILE ONLY in UNITY_INCLUDE_TESTS or DEVELOPMENT_BUILD — never in release.
// All mutating methods are instance-scoped via zoneInstanceId to prevent configuration
// bleed between zone instances in parallel-instance tests.
public interface IZoneTestConfigurator
{
    // Override the zone entry-point coordinates used for ghost-death respawn (EC-NET-1, AC-NC-33a).
    // Coordinates use fixed-point encoding matching wire schema CR-NET-7.2:
    // posX/posY/posZ are centimeters (multiply by 0.01 for world-unit meters).
    void SetZoneEntryPoint(uint zoneInstanceId, short posX, short posY, short posZ);

    // Override the zone player capacity for overflow tests (AC-NC-24).
    // Setting to N means the (N+1)th join is rejected with an overflow response.
    void SetZoneCapacity(uint zoneInstanceId, int maxPlayers);

    // Read the current ST-NET-2 zone state for assertion in zone state machine ACs.
    // Returns the ZoneState enum value as of the last tick boundary.
    ZoneState GetCurrentZoneState(uint zoneInstanceId);

    // Read the current server-side position of an entity in fixed-point encoding.
    // Returns centimeter values (multiply by 0.01 for world-unit meters).
    // Used for AC-NC-33a ghost-death respawn position assertion.
    (short posX, short posY, short posZ) GetEntityPosition(uint entityId);

    // Override LastBeatServerTick for a specific entity — for OWL compensation tests.
    // Injects a specific tick number as if the entity's Beat fired at that tick.
    // Used by AC-NC-29 (networking-core.md) and AC-OWL-01–05 (networking-owl-compensation.md).
    void SetLastBeatServerTick(uint entityId, uint serverTickNumber);

    // Override the server's estimated OWL for a specific entity's client connection.
    // owlSeconds: one-way latency in seconds. Bypasses RTT probe computation.
    // Must be within [0, MAX_COMPENSATABLE_OWL_MS / 1000] to keep compensation active.
    // Used by AC-OWL-05.
    void SetClientOWL(uint entityId, float owlSeconds);

    // Reset all configuration overrides to zone template defaults for the given instance.
    void Reset(uint zoneInstanceId);
}
```

*Supported ACs:* AC-NC-33a (entry-point coordinates via `SetZoneEntryPoint`; respawn position assertion via `GetEntityPosition`), AC-NC-24 (zone capacity override via `SetZoneCapacity`), AC-NC-14 (zone Draining assertion via `GetCurrentZoneState`), AC-NC-41 (zone Active→Closed via `GetCurrentZoneState`), AC-NC-29 and AC-OWL-01–05 (`SetLastBeatServerTick`, `SetClientOWL`).

---

**INetworkTestObserver** *(formerly referenced as IClientTestObserver)*

Captures structured values at the server serialization boundary and at the client transport boundary. Enables deterministic assertion of server-computed values against client-received values without relying on log scraping or rendering-layer inspection.

```csharp
// COMPILE ONLY in UNITY_INCLUDE_TESTS or DEVELOPMENT_BUILD — never in release.
public interface INetworkTestObserver
{
    // --- Server-side capture ---

    // Called when the server serializes a DamageEvent, before it is placed in the R-U batch.
    // Records (attackerEntityId, targetEntityId, serverComputedDamage) for later assertion.
    void OnServerDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage);

    // Called when the server serializes a SelfDamageEvent for the attacker's R-OD path (MCR-2).
    // SelfDamageEvent is a distinct message from DamageEvent — fires once per R-OD serialization.
    // Used by wire-protocol AC-CCR-04 to verify correct server-side routing decision.
    void OnServerSelfDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage);

    // Called when the server places a GoldSyncEvent sub-message into the R-U batch.
    // Records (characterId, newBalance, version) for later assertion.
    void OnServerGoldSyncBatched(uint characterId, uint newBalance, uint version);

    // Called when the server emits a KillEvent on the priority path.
    void OnServerKillEventEmitted(uint killerEntityId, uint targetEntityId, int finalDamage);

    // Called when the server serializes a CycleTimerBroadcast for an entity (U-U path, 0x0103).
    // cyclePositionTicks: entity's current auto-attack cycle position in server ticks (0–BEAT_TICKS).
    // Fires every tick per active entity. Verifies Pillar 2 invariant: CycleBroadcast never omitted
    // under R-U or R-OD congestion (PA-P7-05 separate-packet fix).
    void OnServerCycleTimerBroadcastSerialized(uint entityId, ushort cyclePositionTicks, uint serverTickNumber);

    // Called when the server serializes an EnhancementOutcomeBroadcast for zone-wide R-OD delivery.
    // Provisional signature — full schema pending Enhancement System GDD.
    void OnServerEnhancementOutcomeSerialized(uint characterId, uint itemId, bool success, byte newEnhancementLevel);

    // Called after the R-U batch for a client is fully serialized each tick.
    // deliveredEntityIds: all EntityIDs included in EntityHealthUpdate sub-messages
    // for this client's batch (self-slot and target-slot, per RFR-1 separation invariant).
    // Used by AC-RFR-01, AC-RFR-03, AC-RFR-07 (networking-relevance-filter.md).
    void OnRUBatchEntityHealthUpdates(uint clientId, IReadOnlyList<uint> deliveredEntityIds);

    // --- Client-side capture ---

    // Called on the client when a DamageEvent is received at the transport boundary,
    // before any rendering or game-logic processing.
    void OnClientDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage);

    // Called on the attacker's client when a SelfDamageEvent is received at the transport boundary (R-OD).
    // Fires only on the attacker's client — never on other zone clients.
    void OnClientSelfDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage);

    // Called on the client when a GoldSyncEvent sub-message is extracted from an R-U batch,
    // before stale-discard comparison AND before any balance update is applied.
    void OnClientGoldSyncReceived(uint characterId, uint newBalance, uint version);

    // Called on the client when a KillEvent is received at the transport boundary.
    void OnClientKillEventReceived(uint killerEntityId, uint targetEntityId, int finalDamage);

    // Called on the client when a CycleTimerBroadcast is received (U-U path, 0x0103).
    void OnClientCycleTimerBroadcastReceived(uint entityId, ushort cyclePositionTicks);

    // Called on the client when an EnhancementOutcomeBroadcast is received at the transport boundary.
    // Fires on ALL zone clients (both the enhancing player and all zone observers) — Pillar 3 social signal.
    // Provisional signature — full schema pending Enhancement System GDD.
    void OnClientEnhancementOutcomeReceived(uint characterId, uint itemId, bool success, byte newEnhancementLevel);

    // --- Zone entry capture ---

    // Called on the client when both dual-gate conditions are met simultaneously:
    // (1) SessionReady has been received AND (2) ZoneStateSnapshot reassembly is complete.
    // Fires exactly once per zone entry per client. Used by AC-ZI-6 to establish the gate-open
    // instant: any outbound game RPCs (MovementIntentMessage, SkillCastRequest, NotifySkillUsed)
    // captured before this callback fires violate the dual-gate invariant (zone-instancing.md CR-ZI-9).
    void OnZoneGateOpened(uint clientId);

    // --- Session lifecycle capture ---

    // Called whenever a session transitions between states (ST-NET-1).
    // trigger is a short label matching the ST-NET-1 trigger column (e.g., "HeartbeatTimeout",
    // "AuthSuccess", "SessionSteal", "ReauthLimitExceeded", "ConnectingTimeout").
    void OnSessionStateTransitioned(uint accountId, SessionState fromState, SessionState toState, string trigger);

    // Called when a prior session is invalidated because a new authenticated connection for the
    // same account arrived (session-stealing per B-NP-7). Fires before the new connection
    // proceeds to Connecting, confirming the ordering guarantee.
    void OnSessionInvalidatedBySteal(uint accountId, uint priorCharacterId);

    // Called on each failed re-authentication attempt while in Reconnecting state (EC-NET-7).
    // attemptNumber is 1-based. remainingAttempts decrements toward 0 before session expiry.
    void OnReAuthAttemptFailed(uint accountId, int attemptNumber, int remainingAttempts);

    // Called when the server completes a character state persistence write (CR-NET-6.5 step 3).
    // Fires before any session resource release, confirming commit-before-release ordering.
    void OnPersistenceWriteCompleted(uint characterId, PersistenceWriteReason reason);

    // Called when the ghost combat TTL expires for an entity (CR-GH-10 in networking-ghost-session.md).
    // Fires on the tick that ghostCombatExpiryTick is reached, before the mob retarget fires.
    void OnGhostCombatTTLExpired(uint entityId, uint disconnectTickNumber, uint expiryTick);

    // Called when the server emits a GhostPromotionEvent to all zone clients (CR-GH-2).
    // Fires once per disconnect that triggers ghost promotion, before ghost-period combat begins.
    void OnGhostPromotionEventEmitted(uint characterId);

    // Called on a zone client when a GhostPromotionEvent is received at the transport boundary.
    void OnClientGhostPromotionEventReceived(uint characterId);

    // Called when the zone instance's state machine transitions (ST-NET-2).
    void OnZoneStateTransitioned(uint zoneInstanceId, ZoneState fromState, ZoneState toState);

    // Called when the server emits a SessionHandshake to a connecting or reconnecting client.
    // Enables AC assertions about delivered character state without wall-clock waits.
    // goldVersion: the GoldSyncEvent.Version seed delivered in the handshake — required to verify
    //   that the client initialises cachedVersion correctly before the first GoldSyncEvent (AC-NC-07 scenario B).
    // Provisional signature pending full SessionHandshake schema (OQ-NC-SER-2 / Character Persistence GDD).
    void OnSessionHandshakeEmitted(uint characterId, bool wasKilledWhileDisconnected,
                                   int goldBalance, uint goldVersion, int level, int currentHp,
                                   int currentMp, int heldFreePoints, byte classType);

    // Called each time the client emits a ZoneSnapshotRequest retransmit (B-NP-8).
    // attemptNumber is 1-based; maxAttempts mirrors MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS.
    void OnSnapshotRetransmitAttempt(uint characterId, int attemptNumber, int maxAttempts);

    // --- Priority path capture ---

    // Called once per R-OD message flushed to the transport layer in a given server tick.
    // messageTypeId is the CR-NET-7.1 MessageTypeID of the transmitted message.
    // tickNumber is the ServerTickNumber at which the flush occurred.
    // Used by AC-NC-43 to assert the 8-message-per-tick cap and verify that the enhancement-
    // outcome exemption correctly places the exempted message at position 0 in tick T.
    void OnPriorityPathMessageFlushed(uint characterId, ushort messageTypeId, uint tickNumber);

    // Called at the end of each server tick, after all tick-driven processing completes.
    // tickNumber is the ServerTickNumber that just completed.
    // Used by AC-NC-04 (200 ticks in 10s), AC-NC-05 (Beat cadence — 20 ticks per Beat),
    // and AC-NC-06 (TTL expiry within one tick boundary).
    void OnTickCompleted(uint tickNumber);

    // Called when the server evaluates the OWL grace window for a NotifySkillUsed RPC.
    // adjustedTimer: the final adjustedCycleTimer value after OWL compensation (seconds).
    // graceTriggers: whether adjustedTimer > BaseGraceThreshold × CycleDuration.
    // Used by AC-OWL-05 and AC-NC-29 (networking-owl-compensation.md / networking-core.md).
    void OnSkillGraceWindowEvaluated(uint entityId, float adjustedTimer, bool graceTriggers);

    // Called when the server emits a StatSnapshotEvent to any client.
    // Used by AC-NC-44 (negative AC: verify StatSnapshotEvent is event-driven,
    // not emitted every tick — count must be 0 over idle 60-second window).
    void OnStatSnapshotEmitted(uint entityId);

    // Called when a NotifySkillUsed RPC is rejected due to rate limiting
    // (NOTIFY_SKILL_USED_RATE_LIMIT_MS enforcement). Used by AC-NC-46.
    void OnSkillUsedRateLimitRejected(uint entityId);

    // --- Query methods ---

    // Returns the cumulative count of outbound messages of the given MessageTypeID emitted by
    // the server to a specific client since the last Reset() or test harness initialization.
    // Counts are incremented at the server serialization boundary — before fault injection,
    // so a fragment that is subsequently dropped by ITransportFaultInjector still counts.
    // AC-ZI-8 usage: pass ZoneStateSnapshotFragment.MessageTypeID to assert the exact fragment
    // count emitted for a joining client matches F-ZI-1 (expected range [17, 19] at max load).
    int GetOutboundMessageCount(uint clientId, ushort messageTypeId);
}

// Production enums — declared UNCONDITIONALLY in a production namespace (NOT inside the test guard).
// SessionState (ST-NET-1) and ZoneState (ST-NET-2) are referenced by production session management
// and zone lifecycle code and must compile in all build configurations.
public enum SessionState : byte
{
    Connecting                  = 0,
    Connected                   = 1,
    Disconnected_SessionActive  = 2,
    Reconnecting                = 3,
    Disconnected_SessionExpired = 4,
}

public enum ZoneState : byte
{
    Empty    = 0,
    Active   = 1,
    Draining = 2,
    Closed   = 3,
}

// Test-only enum — declared inside #if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD alongside INetworkTestObserver.
// Only referenced by OnPersistenceWriteCompleted — not needed in production code.
public enum PersistenceWriteReason : byte
{
    SessionExpiry        = 0,
    ExplicitDisconnect   = 1,
    SessionSteal         = 2,
    ZoneClose            = 3,
    GhostCombatTTLExpiry = 4,  // ghost combat TTL expired; character state written per CR-GH-10
    GhostDeath           = 5,  // ghost entity HP reached zero; pre-disconnect snapshot HP persisted per CGS-5
}
```

*Supported ACs:* AC-NC-03 (`OnServerDamageEventSerialized`, `OnClientDamageEventReceived`), AC-NC-04/05/06 (`OnTickCompleted`), AC-NC-07 (`OnClientGoldSyncReceived`), AC-NC-10 (`OnSessionStateTransitioned`), AC-NC-11 (`OnSessionHandshakeEmitted`), AC-NC-12 (`OnSessionStateTransitioned`, `OnPersistenceWriteCompleted`), AC-NC-19 (`OnServerGoldSyncBatched`, `OnClientGoldSyncReceived`), AC-NC-29 (`OnSkillGraceWindowEvaluated`), AC-NC-30 (`OnServerKillEventEmitted`, `OnClientKillEventReceived`, `OnClientDamageEventReceived`), AC-NC-37 (`OnSessionInvalidatedBySteal`, `OnSessionStateTransitioned`, `OnPersistenceWriteCompleted`), AC-NC-38 (`OnReAuthAttemptFailed`, `OnSessionStateTransitioned`), AC-NC-39 (`OnSessionStateTransitioned`), AC-NC-40 (`OnSnapshotRetransmitAttempt`), AC-NC-41 (`OnZoneStateTransitioned`, `OnPersistenceWriteCompleted`), AC-NC-42 (`OnPersistenceWriteCompleted`), AC-NC-43 (`OnPriorityPathMessageFlushed`), AC-NC-44 (`OnStatSnapshotEmitted`), AC-NC-46 (`OnSkillUsedRateLimitRejected`), wire-protocol AC-CCR-04 (`OnServerSelfDamageEventSerialized`, `OnClientSelfDamageEventReceived`), AC-GH-1 (`OnGhostPromotionEventEmitted`, `OnClientGhostPromotionEventReceived`), AC-OWL-05 (`OnSkillGraceWindowEvaluated`), **AC-ZI-6** (`OnZoneGateOpened`), **AC-ZI-8** (`GetOutboundMessageCount`).

---

### Release-Build Stripping

All four interfaces (`ITransportFaultInjector`, `IServerCrashInjector`, `IZoneTestConfigurator`, and `INetworkTestObserver`) and all their implementations must be **absent from release builds**. Stripping is enforced by conditional compilation:

```csharp
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
    // Interface declarations and their concrete implementations go here.
    // Anything inside this block is stripped from Player builds.
#endif
```

**Enforcement rules:**

1. All interface declarations, concrete implementations, and injection registration call sites must be inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` blocks.
2. No production code path may call methods on these interfaces without being inside a conditional block — this prevents accidental coupling of production logic to test interfaces.
3. Registration of the concrete implementations into the dependency injection container must also be conditional — a `NetworkingTestHarness.RegisterInterfaces()` call that is only invoked in test/dev builds.
4. A CI release-build smoke test must assert that the release binary does not contain the strings `ITransportFaultInjector`, `IServerCrashInjector`, `IZoneTestConfigurator`, or `INetworkTestObserver` (IL2CPP strips by type name; this confirms no reference survives stripping).

**Why this matters:** If these interfaces are present in release builds, malicious clients could use them to inject faults into their own transport or simulate crashes, undermining the server-authoritative model. The release-build stripping requirement is not optional.

---

### Updated AC Preconditions

The following acceptance criteria from `networking-core.md`, `networking-session.md`, and `networking-wire-protocol.md` have preconditions that require test harness interfaces. Updated precondition language is canonical here:

**AC-NC-07 precondition (updated — B-QA-SYSTEMIC):** *Precondition: Requires `ITransportFaultInjector.ReorderNext(GoldSyncEvent.MessageTypeID)` to inject message reordering in the test environment. "Simulated network environment with artificial packet reordering" means this injector call — not physical network degradation or OS-level packet manipulation.*

**AC-NC-30 precondition (updated — B-QA-SYSTEMIC):** *Precondition for scenario (b) (discard DamageEvent after KillEvent): Requires `ITransportFaultInjector.DelayNextOutbound(DamageEvent.MessageTypeID, 1, 200)` to hold the damage event in transit past the KillEvent arrival. Requires `INetworkTestObserver.OnClientKillEventReceived` and `OnClientDamageEventReceived` to assert arrival order. "Artificial packet reordering" means this injector call.*

**AC-NC-35 precondition (updated — B-QA-SYSTEMIC):** *Precondition: Requires `ITransportFaultInjector.DropSnapshotFragment(ushort.MaxValue)` to drop the last fragment of a ZoneStateSnapshot regardless of `totalFragments` count (dynamic per CR-NET-7.3 — `totalFragments` is not known before transmission begins). "Simulated transport-layer loss" means this injector call — not physical packet loss or OS-level manipulation.*

**Wire-protocol AC-NC-36 precondition (updated — B-QA-SYSTEMIC):** *Precondition: Requires `ITransportFaultInjector.SetSequenceNumber(4294967293)` to seed the outbound SequenceNumber counter 3 below `uint.MaxValue`. Must be called before any message is emitted on the test connection. "SequenceNumber approaching uint.MaxValue" means this injector call — not manual counter state manipulation.*

---

## Formulas

None. This document specifies test infrastructure contracts, not game formulas.

---

## Edge Cases

**Test interfaces invoked outside test builds:** Any call site that reaches a test interface method in a release build is a build configuration error. Unity Player builds with `IL2CPP` will strip the types entirely — surviving references do **not** cause compile errors; IL2CPP silently omits stripped types. A reference that escapes stripping produces `TypeNotFoundException` at runtime without warning. The CI release-build name-scan in enforcement rule 4 is the actual safety net — do not rely on compile-time signals to detect surviving references. Do not add null-guard fallbacks for test interface calls in production code.

**`IServerCrashInjector` crash timing:** When `CrashStep.AfterPersistenceWrite` is registered, the crash fires synchronously within the same call stack that completes the persistence write — before any message is queued for transmission. This is guaranteed by the interface contract; implementations must not use deferred/async patterns to trigger the crash, as that would introduce a race with the transmission step.

---

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-core.md` | Parent — commit-before-broadcast (CR-NET-5) is the primary contract tested by `IServerCrashInjector` |
| `networking-session.md` | `IServerCrashInjector` required for AC-NC-16, AC-NC-34; `ITransportFaultInjector` for AC-NC-35 |
| `networking-wire-protocol.md` | `INetworkTestObserver` captures at the serialization boundary defined by CR-NET-7; `ITransportFaultInjector` operates at the channel level defined by CR-NET-3 |
| `networking-ghost-session.md` | `OnGhostCombatTTLExpired` fires on CR-GH-10 TTL expiry; `OnGhostPromotionEventEmitted` fires on CR-GH-2 promotion; `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` required for AC-GH-10 |
| `networking-owl-compensation.md` | Primary consumer — AC-OWL-05 requires `SetLastBeatServerTick` and `OnSkillGraceWindowEvaluated`; `SetClientOWL` also defined here for OWL injection tests |
| `networking-relevance-filter.md` | Consumer — AC-RFR-01, AC-RFR-03, AC-RFR-07 require `OnRUBatchEntityHealthUpdates` to verify per-client `EntityHealthUpdate` delivery each tick |
| `zone-instancing.md` | Consumer — AC-ZI-6 requires `OnZoneGateOpened` for dual-gate invariant verification; AC-ZI-8 requires `GetOutboundMessageCount` for snapshot fragment count assertion |

---

## Tuning Knobs

None specific to this document. Test configurations (e.g., `HEARTBEAT_TIMEOUT_SECONDS = 3s` for CI speed) are documented at each relevant AC in `networking-session.md` and `networking-core.md`.

---

## Acceptance Criteria

**AC-NC-43 (Logic — B-QA-6) — Priority-path 8-message cap enforcement**

Given a test client for which the server has 12 R-OD messages queued simultaneously (via `ITransportFaultInjector.DelayNextOutbound` holding 12 events until tick boundary), When tick T fires, Then:
- Exactly 8 `OnPriorityPathMessageFlushed` callbacks fire with `tickNumber == T` for this client
- Exactly 4 `OnPriorityPathMessageFlushed` callbacks fire with `tickNumber == T+1` for this client, in the same relative emission order as the deferred messages held in the tick-T queue
- No message is dropped — all 12 callbacks eventually fire across tick T and tick T+1

Additionally: When the queued 12 messages include at least one `EnhancementOutcomeBroadcast`, Then:
- The first `OnPriorityPathMessageFlushed` callback in tick T has `messageTypeId` matching `EnhancementOutcomeBroadcast.MessageTypeID`, regardless of that message's position in emission order
- The message that held position 0 in emission order (if it was not the enhancement outcome) appears as the first `OnPriorityPathMessageFlushed` callback in tick T+1

*Unit-testable without physical transport — inject 12 pre-built R-OD message objects into the priority queue and advance one tick.*

---

**AC-TC-01 — Release-build stripping verification**

Given a Player build (IL2CPP, Release configuration), When the build output is inspected for type names, Then the strings `ITransportFaultInjector`, `IServerCrashInjector`, `IZoneTestConfigurator`, and `INetworkTestObserver` are not present in any assembly. *Automated: run as a post-build CI step using `grep` or `strings` on the output assembly.*

---

**AC-TC-02 (Static Analysis — B-QA-SYSTEMIC) — Test interface call site isolation**

Given the production source tree (`src/`), When a static analysis pass is run on all `.cs` files outside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` blocks, Then no call site references a method on `ITransportFaultInjector`, `IServerCrashInjector`, `IZoneTestConfigurator`, or `INetworkTestObserver`. *Automated: run as a pre-build CI step using a Roslyn analyzer or grep script that identifies interface method calls outside conditional compilation guards. Does not require a full IL2CPP build — can run on source before compilation.*

---

## Open Questions

None currently open for this document. The interface contracts above are sufficient to implement the test harness. The Networking ADR must confirm whether the chosen library provides a transport-level fault injection hook that `ITransportFaultInjector` can delegate to, or whether a custom transport wrapper is required.
