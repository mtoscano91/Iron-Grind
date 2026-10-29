# Story 002: MovementIntentMessage Wire Type and Codec

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (3 gaps) → fixed → Ready
> **Layer**: Core (codec in Foundation)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-10; `design/gdd/networking-wire-protocol.md` — Movement System Messages (`MovementIntentMessage`, 10-byte body), CR-NET-7.1 (C→S envelope + SenderEntityID), CR-NET-7.2 (unit direction ×32,767)
**Requirement**: `TR-mov-003` (codec part)

**ADR Governing Implementation**: ADR-014 (inbound request table: `MovementIntentMessage` U-U, request, not held, body 10); ADR-004 (project envelope over NGO custom messages)
**ADR Decision Summary**: client requests travel in the project envelope; the routing row declares channel, direction and body size; the intake checks the body length against the descriptor.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure codec)
**Engine Notes**: none — `Span<byte>` / `BinaryPrimitives`, as the other codecs.

**Control Manifest Rules (this layer)**:
- Required: explicit little-endian codec, no reflection serializers, no `BinaryFormatter` / `JsonUtility` (wire protocol CR-NET-7.8)
- Required: every message type has one `MessageRoutingRegistry` row
- Forbidden: allocation in encode/decode

---

## Acceptance Criteria

- [ ] `MovementIntentMessage` readonly struct in `src/Foundation/Networking/WireProtocol/`: `DirX` (short), `DirZ` (short), `FacingAngle` (short, ×10), `TickNumber` (uint); `BodySize = 10`.
- [ ] `MessageTypeId = 0x0211` (assigned at `/story-readiness` 2026-10-29 — unused in `src/`, `design/`, `docs/`; next to `HeartbeatMessage` 0x0210, the other U-U client→server type). Recorded in `networking-wire-protocol.md` (Movement System Messages note).
- [ ] `MovementIntentMessageCodec.Write` / `TryRead`: exact 10-byte body (any other length → false), field order dirX, dirZ, facingAngle, tickNumber, little-endian.
- [ ] Direction helpers: `EncodeDirection(Vector2 unitXZ, out bool wasNonUnit)` → shorts ×32,767 (rounded, clamped to ±32,767); `DecodeDirection(short, short)` → floats ÷32,767. An exact zero vector encodes to (0, 0) with `wasNonUnit = false`. **Pre-encode guard (wire GDD, Movement System Messages note):** for a non-zero input, if `sqrt(dirX² + dirZ²)` of the encoded shorts differs from 32,767 by more than ±100, the result is `(32767, 0)` and `wasNonUnit = true`. The codec does not log — the caller (Story 012 producer) logs the anomaly.
- [ ] Facing helpers: degrees → `RoundToInt(deg × 10)` wrapped into 0–3599; decode ÷10. **A received `FacingAngle` outside 0–3599 is wrapped, not rejected** (decided at readiness 2026-10-29; AC-NC-32 precedent: substitute, never drop): `NormalizeFacing(short)` = `((v % 3600) + 3600) % 3600`, so every short maps to a valid angle and the message's direction still applies. `TryRead` checks only the body length; the intake (Story 004) calls `NormalizeFacing` before storing.
- [ ] `MessageRoutingRegistry` row: U-U, `ClientToServer`, body 10, rationale "self-corrects next tick (MCR-2)"; the architecture routing-table test passes.

---

## Implementation Notes

- Follow `HeartbeatMessage` / `SetTarget` for struct + codec layout and `EquipRequestCodec` for the exact-length rule.
- The 4-byte `SenderEntityID` is part of the C→S envelope (`ClientEntityMessageEnvelope`), not of this body.
- Add the new types to the ADR-012 allow-list (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`) if the boundary test asks for it (the Equipment 008 run failed on this first).

---

## Out of Scope

- Story 004: registering the inbound handler, sender check, stale discard
- Story 012: the client building and sending the message

---

## QA Test Cases

- **Round trip**: Given dir (0.6, −0.8), facing 123.4°, tick 1000 — When written then read — Then dirX 19660, dirZ −26214 (rounded), facing 1234, tick 1000; decoded direction within 1/32,767 of the input.
- **Exact length**: bodies of 9 and 11 bytes → `TryRead` false; 10 → true.
- **Direction edges**: (1, 0) → (32767, 0); (−1, 0) → (−32767, 0); (0, 0) → (0, 0); a slightly-over-unit input (1.00001, 0) → clamped 32767, `wasNonUnit` false; (0.6, −0.8) → `wasNonUnit` false; a non-unit input (0.5, 0) → (32767, 0), `wasNonUnit` true.
- **Facing edges**: 0° → 0; 359.94° → 3599; 359.96° → wraps to 0; −0.1° → 3599; `NormalizeFacing`: 3600 → 0, 3599 → 3599, −1 → 3599, 7200 → 0, `short.MinValue` (−32768) → 3232, `short.MaxValue` (32767) → 367.
- **Routing row**: the registry has exactly one row for the new ID with U-U and `ClientToServer`.
- **No allocation**: write + read inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/MovementIntentMessage_Codec_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 004, 012
