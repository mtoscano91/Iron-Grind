# Story 008: Heartbeat Message & IL2CPP AOT Guardrails

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md`
**Requirement**: `TR-net-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: The project's serialization is retained independent of NGO (Decision 4) specifically so IL2CPP AOT constraints are controlled by the project, not by NGO's generic dispatch internals.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: HIGH
**Engine Notes**: IL2CPP AOT forbids generic serializer dispatch (`typeof(T)`), `[StructLayout(LayoutKind.Explicit)]` without device testing, `BinaryFormatter`/`JsonUtility`, `System.Reflection.Emit`, LINQ on hot paths, and lambda-capture handler registration. These are hard constraints, not style preferences — violating them risks `MissingMethodException` or silent corruption on-device that never reproduces in the Editor.

**Control Manifest Rules (Foundation layer)**:
- Forbidden: generic serializers using `typeof(T)` dispatch; `[StructLayout(LayoutKind.Explicit)]` without device testing; `BinaryFormatter`/`JsonUtility`; boxing on hot path; interface/virtual dispatch on generic value-type parameters; `System.Reflection.Emit`; LINQ on dispatch/serialization hot paths; lambda-capture handler registration — source: CR-NET-7.8

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story:*

- [ ] **AC-HB-1** [BLOCKING] (`HeartbeatMessage` schema, CR-NET-7.10): `HeartbeatMessage` is a client→server keep-alive with no body fields (10-byte wire size = envelope only), sent on the U-U channel, standalone (not batched). Mere receipt resets the server's inactivity timeout.
- [ ] **AC-NC-38** [BLOCKING] (Integration, wire-protocol's numbering — skip-on-activity semantics): Given a test client that sends a `NotifySkillUsed` RPC in tick T, when tick T completes and the heartbeat timer has not yet elapsed, then no `HeartbeatMessage` is emitted for tick T (the RPC resets the heartbeat counter). When no outbound RPC has been sent for exactly `HEARTBEAT_INTERVAL_SECONDS` after the last packet, a `HeartbeatMessage` is sent; a second is not sent until another full interval of silence elapses.
- [ ] **AC-AOT-1** [BLOCKING] (Static Analysis): Given the `src/Foundation/Networking` (or equivalent) assembly, when a static analysis pass runs, then no serializer method uses `typeof(T)`-based generic dispatch, no `BinaryFormatter`/`JsonUtility` call sites exist for wire messages, and no `event`/delegate handler registration in this assembly uses a lambda that closes over a heap object (matches the ADR-010 lambda-capture prohibition, extended here to serialization callbacks).

---

## Implementation Notes

*Derived from CR-NET-7.8/7.10:*

- `HeartbeatMessage` is the simplest possible schema — envelope only, no payload — implement its send/receive path as a template for "standalone, unbatched, U-U" messages other stories may reuse the pattern from.
- Skip-on-activity: track "last outbound packet timestamp" per connection (tick-based, not wall-clock — reuse Story 005's tick-based comparison pattern); if any packet was sent within the current `HEARTBEAT_INTERVAL_SECONDS` window, skip the heartbeat send for that window.
- The AOT guardrail check (AC-AOT-1) is a project-wide static analysis rule, not a runtime behavior — implement as a lightweight Roslyn analyzer or grep-based CI script (can share infrastructure with Story 002's AC-TC-02 static analysis, different rule set).
- This story is a natural place to also document (via code comments/analyzer rule descriptions) the concrete list of forbidden constructs from CR-NET-7.8, since every future wire-message implementer needs this checklist.

---

## Out of Scope

*Handled by neighbouring stories:*

- `SelfPositionUpdate` and other CSP-related messages — deferred; Client-Side Prediction GDD is not yet approved (see EPIC.md scoping note)
- Envelope/primitive encoding — Story 003
- The batch messages this heartbeat mechanism coexists with — Story 007

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_Heartbeat_AOT_tests.cs`

- **AC-HB-1**: Given a connection with no other outbound traffic, when `HEARTBEAT_INTERVAL_SECONDS` elapses, then exactly one 10-byte `HeartbeatMessage` is emitted.
- **AC-NC-38**: Given a `NotifySkillUsed` RPC sent within the heartbeat window, then no heartbeat fires that window; given exactly one interval of subsequent silence, exactly one heartbeat fires, and not a second until another full interval passes.
- **AC-AOT-1**: Given the networking assembly source, when the static analyzer runs, then zero violations of the CR-NET-7.8 forbidden-construct list are found; a deliberately-introduced violation (test fixture) is correctly flagged.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/WireProtocol_Heartbeat_AOT_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (envelope), Story 005 (tick-based timing pattern)
- Unlocks: Story 012 (heartbeat timeout drives the Connected→Disconnected_SessionActive transition)
