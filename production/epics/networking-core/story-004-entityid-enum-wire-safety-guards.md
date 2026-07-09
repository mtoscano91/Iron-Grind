# Story 004: EntityID/Enum Wire-Safety Guards

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
**ADR Decision Summary**: Same as Story 003 — project-owned serialization. This story adds the two safety invariants the wire format depends on: no `Invalid` (0) ID ever reaches the wire, and no unknown enum byte ever crashes or silently corrupts state.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: `Enum.IsDefined()` is forbidden on IL2CPP (CR-NET-7.4) — range-checks must be explicit numeric comparisons, not reflection-based.

**Control Manifest Rules (Foundation layer)**:
- Required: `EntityID`/`ItemID`/`CharacterID` serialize as 4-byte uint little-endian; `0` (Invalid) must never appear in a valid message body — serializer must assert before writing — source: CR-NET-7.3
- Required: all game-message enums declare explicit underlying type (`enum : byte` for <256 values); receivers must range-check bytes before casting — source: CR-NET-7.4
- Forbidden: `Enum.IsDefined()` for enum validation on IL2CPP — source: CR-NET-7.4
- Forbidden: `Serialize<T>()` reflection-based generic ID serializers — source: CR-NET-7.3 (IL2CPP AOT safety; concrete non-generic methods only)

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md`, scoped to this story:*

- [ ] **AC-NC-31** [BLOCKING] (Logic — wire-protocol's AC-NC-31, distinct from the root GDD's same-numbered hysteresis AC owned by Story 024; a documentation ID collision, not a duplicate requirement): Given the serialization layer encoding a valid game message where any ID field (`EntityID`/`ItemID`/`CharacterID`) is `0` (Invalid), when the serializer attempts to write the message, then it throws before writing any bytes, an `InvalidIdZeroWrite` anomaly is logged with message type and field name, and no partial message appears in the buffer.
- [ ] **AC-NC-32** [BLOCKING] (Logic — enum range-check fallback): Given a receiver processing a `DamageType` field with byte value `100` (outside declared range 0–2) and a `DisconnectType` field with byte value `50` (outside declared range 0–2), when each is range-checked and cast, then `DamageType` substitutes `Physical=0` and processing continues; `DisconnectType` substitutes `Timeout=1` and the entity is still despawned; both substitutions are logged as anomalies with the received byte value and message type. No message is silently dropped for an unknown enum byte.
- [ ] **AC-NC-18** [BLOCKING]: Given a test client sending an `AllocateFreePointRequest` with `StatID` set to a byte value not present in the `StatID` enum (e.g. `0xFF`), when the server receives it, then the message is dropped, no stat change is applied, the anomaly is logged, and the server does not crash. *Notes the CR-NET-7.4 cross-doc blocker: Character Stats GDD's `StatID` must change from `enum : uint` to `enum : byte` before this can transmit natively as a byte — until that GDD amendment lands, transmit as byte with explicit range validation per the workaround already specified in the wire-protocol GDD.*
- [ ] **AC-NC-17** [BLOCKING] (Integration — deterministic load fixture): Given a deterministic fixture driving the server through exactly 500 ticks with 10 scripted entities exchanging `DamageEvent` every tick, when all emitted sub-messages are inspected, then at least 5,000 `DamageEvent` sub-messages are captured and no `attackerEntityId`/`targetEntityId` field contains `0`.

---

## Implementation Notes

*Derived from CR-NET-7.3/7.4:*

- ID serializers are concrete, non-generic methods per type (`SerializeEntityId`, `SerializeItemId`, `SerializeCharacterId`) — never a `Serialize<T>()` reflection dispatch (IL2CPP AOT safety).
- The zero-ID assert must fire *before* any byte is written for that message — a partial write followed by a thrown exception would leave a corrupt buffer state.
- Enum range-check pattern: `if (rawByte > maxDeclaredValue) { LogAnomaly(...); rawByte = fallbackValue; }` then cast — never `Enum.IsDefined()`.
- Each enum with wire representation needs its own documented fallback value (from `networking-wire-protocol.md`'s Enum Types section): `DamageType`→`Physical=0`, `DisconnectReason`→`Other=255`, `DisconnectType`→`Timeout=1`. `StatID` needs the same treatment but is currently blocked on the Character Stats GDD's underlying-type change — implement the byte-transmission-with-range-validation workaround now (do not wait on the other GDD), and flag the residual cross-doc dependency in this story's Completion Notes when closed.

---

## Out of Scope

*Handled by neighbouring stories:*

- The Character Stats GDD amendment itself (`enum : uint` → `enum : byte`) — out of scope for Networking Core; flag as a cross-epic dependency, do not edit `character-stats.md` from this story
- Envelope/fixed-point primitive encoding — Story 003
- Version/tick stale-discard — Story 005

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_EntityIdEnumGuards_tests.cs`

- **AC-NC-31**: Given a message with a zero ID field, when serialized, then an exception is thrown, an `InvalidIdZeroWrite` anomaly is logged, and the output buffer contains zero bytes for that message.
- **AC-NC-32**: Given out-of-range `DamageType`/`DisconnectType` byte values, when deserialized, then the documented fallback values are substituted, processing continues (entity despawn still occurs for `DisconnectType`), and both are logged as anomalies.
- **AC-NC-18**: Given `StatID = 0xFF` in an `AllocateFreePointRequest`, when the server receives it, then the message is dropped, no stat mutation occurs, and no exception propagates.
- **AC-NC-17**: Given the 500-tick/10-entity deterministic fixture, when run to completion, then ≥5,000 `DamageEvent` sub-messages are captured with zero `0`-valued ID fields.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/WireProtocol_EntityIdEnumGuards_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (uses its primitive encoder foundation)
- Unlocks: All downstream message schemas that carry ID or enum fields
