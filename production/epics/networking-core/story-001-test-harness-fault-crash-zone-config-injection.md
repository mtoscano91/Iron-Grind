# Story 001: Test Harness — Fault/Crash/Zone-Config Injection

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-test-harness.md`
**Requirement**: `TR-net-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: NGO is the connection/transport substrate; project owns serialization. Test interfaces sit below game logic, above physical transport — they must compile only in test/dev builds (never release), per the GDD's own enforcement rules, since a surviving reference in a release build would let malicious clients inject faults into their own transport.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: HIGH (IL2CPP stripping behavior)
**Engine Notes**: IL2CPP silently strips types inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` guards from Player builds — a surviving reference produces a `TypeNotFoundException` at runtime with no compile-time warning. Do not rely on compile-time signals to detect surviving references (Story 002 owns the CI verification).

**Control Manifest Rules (Foundation layer)**:
- Guardrail: all four test-harness interfaces and their concrete implementations must be inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` blocks — source: `networking-test-harness.md`
- Forbidden: any production code path calling these interfaces without being inside the conditional block

---

## Acceptance Criteria

*From `design/gdd/networking-test-harness.md`, scoped to this story — these three interfaces are the fault/crash/config injection surface; `INetworkTestObserver` is Story 002.*

- [x] **AC-TH-1** [BLOCKING]: `ITransportFaultInjector` compiles with exactly these members: `DropNextOutbound(ushort messageTypeId, int count)`, `DelayNextOutbound(ushort messageTypeId, int count, int delayMs)`, `ReorderNext(ushort messageTypeId)`, `DropSnapshotFragment(ushort fragmentIndex)` (where `ushort.MaxValue` drops the last fragment regardless of `totalFragments`), `SetSequenceNumber(uint value)` (takes effect atomically at the next tick boundary if called mid-stream), `Reset()`.
- [x] **AC-TH-2** [BLOCKING]: `IServerCrashInjector` compiles with `RegisterCrashAt(CrashStep step)`, `ClearRegistered()`, and `enum CrashStep : byte { AfterPersistenceWrite=0, AfterOutcomeEmit=1, AfterPersistenceWriteRespec=2, AfterTTLExpiryPersistenceWrite=3, AfterGhostCleanupPersistenceWrite=4 }`. When `CrashStep.AfterPersistenceWrite` is registered, the crash fires synchronously within the same call stack that completes the persistence write — before any message is queued for transmission (no deferred/async trigger).
- [x] **AC-TH-3** [BLOCKING]: `IZoneTestConfigurator` compiles with `SetZoneEntryPoint(uint zoneInstanceId, short posX, short posY, short posZ)`, `SetZoneCapacity(uint zoneInstanceId, int maxPlayers)`, `GetCurrentZoneState(uint zoneInstanceId) : ZoneState`, `GetEntityPosition(uint entityId) : (short posX, short posY, short posZ)`, `SetLastBeatServerTick(uint entityId, uint serverTickNumber)`, `SetClientOWL(uint entityId, float owlSeconds)`, `Reset(uint zoneInstanceId)`. All mutating methods are instance-scoped via `zoneInstanceId` to prevent bleed between parallel-instance tests.
- [x] **AC-TH-4** [BLOCKING]: All three interfaces, their concrete implementations, and their DI registration call sites compile only inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`. `SessionState` and `ZoneState` enums are declared unconditionally (production code references them); `CrashStep` is declared inside the guard.

---

## Implementation Notes

*Derived from `networking-test-harness.md`:*

- These interfaces sit "below game logic, above physical transport" — implementations intercept outbound/inbound message flow at the serialization boundary, not at the OS socket layer.
- `SetSequenceNumber` must integrate with the envelope's per-connection counter (Story 005) — calling it mid-stream takes effect atomically at the next tick boundary, never mid-tick.
- `IZoneTestConfigurator.GetEntityPosition`/`SetZoneEntryPoint` use the same centimeter fixed-point encoding as the wire format (`×0.01 = meters`) — reuse the Story 003 primitive encoder, do not reimplement.
- `SetLastBeatServerTick`/`SetClientOWL` exist here as injection points for the OWL compensation stories (022–023) — implement the setters now even though the OWL algorithm they feed doesn't exist yet; this story only needs to store and expose the override value.
- `IServerCrashInjector`'s crash must be genuinely synchronous — a `Task`-based or `Invoke`-deferred crash would introduce a race with the transmission step the tests are designed to catch.

---

## Out of Scope

*Handled by neighbouring stories:*

- `INetworkTestObserver` and release-build stripping CI verification — Story 002
- The actual persistence/crash recovery logic these interfaces test — Stories 011, 015, 018–021
- The OWL compensation algorithm these interfaces feed test data into — Stories 022–023

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs`

- **AC-TH-1**: Given a test harness instance, when each `ITransportFaultInjector` method is called with valid arguments, then no exception is thrown and internal state reflects the call (e.g. `DropNextOutbound(msgType, 2)` followed by two outbound sends of that type results in zero delivered messages of that type).
- **AC-TH-2**: Given `IServerCrashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite)`, when a simulated persistence write completes, then the crash fires before the outcome message is queued — verify via a call-order assertion (crash callback invoked, then assert no message was ever enqueued).
- **AC-TH-3**: Given two zone instance IDs, when `SetZoneCapacity` is called for instance A only, then instance B's capacity is unaffected (`Reset(A)` does not affect B's overrides either).
- **AC-TH-4**: Given a Player release build configuration (verified in Story 002's CI check), these three interfaces are absent from the compiled output.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs` — must exist and pass

**Status**: [x] Created — 31 test methods, all 4 ACs covered

---

## Dependencies

- Depends on: None
- Unlocks: All other Networking Core stories (011 through 029) reference these interfaces in their own QA Test Cases

## Completion Notes
**Completed**: 2026-07-08
**Criteria**: 4/4 passing
**Deviations**: None blocking. Advisory: TR-net-009 not yet in `tr-registry.yaml` (pre-existing project-wide registry gap, not new); `NetworkingTestHarness` implemented as a guarded static factory rather than the GDD's literal `RegisterInterfaces()` wording, to respect ADR-010's ban on service-locator/EventBus singleton patterns — functionally equivalent for the compile-guard/stripping requirement, endorsed by code review.
**Test Evidence**: Logic — `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs`, 31 test methods
**Code Review**: Complete — verdict CHANGES REQUIRED (one fix: `Reset()` wasn't clearing the `_hasEmittedAnyMessage` mid-stream flag, contradicting its own "clean state" doc comment) → fixed, with a new regression test added → APPROVED WITH SUGGESTIONS. Non-blocking suggestions deferred: additive-`DropNextOutbound` test coverage; an obscure specific-index/last-fragment-sentinel overlap edge case in `TryConsumeSnapshotFragmentDrop`.
