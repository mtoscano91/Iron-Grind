# Story 003: Message Envelope & Fixed-Point Primitive Serialization

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md`
**Requirement**: `TR-net-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 4 — the project's 10-byte envelope and serialization are owned by the project, not NGO. Hot-path messages send through NGO's custom messaging API with the project's serialized envelope+payload as the message body; NGO's own framing (connection ID, its headers) is disjoint from and wraps the project envelope.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: HIGH
**Engine Notes**: `NetworkDelivery` enum member names and `CustomMessagingManager` send API are post-cutoff and must be verified against `docs/engine-reference/unity` before coding (ADR-004 Engine Compatibility). This story only implements the project-owned envelope/primitive encoding — it does not select the specific NGO send call (that's Story 006's concern for priority-path, and downstream systems' concern for their own messages).

**Control Manifest Rules (Foundation layer)**:
- Required: 10-byte envelope (`MessageTypeID` ushort, `SequenceNumber` uint, `ServerTickNumber` uint); +4-byte `SenderEntityID` for client→server messages referencing a runtime entity — source: CR-NET-7.1
- Required: all primitives fixed-width little-endian, no variable-length encoding at MVP — source: CR-NET-7.2
- Forbidden: raw `float`/`Vector3`/`Quaternion` in any network message body — source: CR-NET-7.2

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story:*

- [ ] **AC-WP-1** [BLOCKING] (envelope, CR-NET-7.1): The envelope serializer produces exactly 10 bytes for server-originated messages (`MessageTypeID` 2B, `SequenceNumber` 4B, `ServerTickNumber` 4B) and exactly 14 bytes for client→server entity-referencing messages (+`SenderEntityID` 4B).
- [ ] **AC-NC-28** [BLOCKING] (Logic — fixed-point round-trip accuracy): Given the server encodes (a) a Vector3 position of `(12.75, 0.00, -85.23)` metres, (b) a Quaternion rotation of `(0.707, 0.0, 0.707, 0.0)`, (c) a `cycleTimer` value of `0.37 × CycleDuration`, when a client decodes these values, then: decoded position is within ±0.01m per axis; decoded quaternion (after renormalization) has dot product ≥0.9999997 with the original; decoded cycleTimer fraction is 0.37 ± 0.0001. A boundary-value test at `(±327.67, 0, 0)` must not overflow; a degenerate-quaternion input `(0,0,0,0)` must encode as identity `(0,0,0,1)` and log an anomaly.
- [ ] **AC-NC-03** [BLOCKING] (Logic — cross-client value consistency): Given two clients connected to the same zone and client A taking damage, when the damage event reaches client B, then the damage value displayed on client B matches the value the server computed (proven at the serialization/deserialization boundary, not gameplay logic).

---

## Implementation Notes

*Derived from CR-NET-7.1/7.2:*

- Fixed-point encodings (all little-endian, no variable-length): `critChance` → ushort ×10,000; `attackSpeedMultiplier` → ushort ×1,000; `cycleTimer` → ushort normalized 0–10,000 with pre-encode guards; `finalDamage` → int, no encoding; Vector3 position → 3×short ×100 (centimeter precision, ±327.67m range, overflow guard — clamp or assert, do not silently wrap); Quaternion → 4×short ×32,767 (normalize/clamp/zero-guard — a `(0,0,0,0)` input must encode as identity and log an anomaly, per AC-NC-28); unit direction vector → 3×short ×32,767 (normalize/zero-guard).
- Authoritative float fields (gold, HP, XP, stats) serialize as integers via `Mathf.FloorToInt` — these are NOT the fixed-point display encodings above; they are already-integer domain values passed through a float API boundary.
- No raw `float`/`Vector3`/`Quaternion` type may appear in any message struct — every position/rotation/percentage field must go through one of the encoders above.
- Build these as pure, allocation-free encode/decode functions (structs in, byte spans out) — this is the foundation every other wire-protocol story's message schema calls into. Do not couple this to NGO's send API; that binding happens per-message in downstream stories.

---

## Out of Scope

*Handled by neighbouring stories:*

- EntityID/enum range-check serialization (CR-NET-7.3/7.4) — Story 004
- Version/SequenceNumber stale-discard comparison (CR-NET-7.5) — Story 005
- Priority-path queue and batch framing (CR-NET-7.6/7.7) — Stories 006–007
- Individual downstream message schemas (Enhancement, Currency, Leveling, etc.) — owned by each system's own future epic, using this story's encoders

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs`

- **AC-WP-1**: Given a server-originated message and a client-originated entity-referencing message, when both are serialized, then their envelope byte lengths are exactly 10 and 14 respectively, with fields at the documented offsets.
- **AC-NC-28**: Given the three worked values in the AC, when encoded then decoded, then all three tolerances hold; given `(±327.67, 0, 0)`, no overflow/exception occurs; given a `(0,0,0,0)` quaternion, the decoded result is `(0,0,0,1)` and an anomaly is logged.
- **AC-NC-03**: Given a server-computed damage value serialized then deserialized through this story's encoders, then the round-tripped value is bit-for-bit identical to the input (int, no lossy encoding involved for `finalDamage`).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (test harness interfaces referenced by sibling stories' tests; not strictly required for this story's own unit tests)
- Unlocks: Stories 004–008 (all build on these primitive encoders); every downstream system's own message schemas
