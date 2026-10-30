# Story 002: HitCheckResult Wire Type and Codec

> **Epic**: Hit Detection
> **Status**: Ready
> **Layer**: Core (codec in Foundation)
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/hit-detection.md` — CR-HD-10; `design/gdd/networking-wire-protocol.md` — `HitCheckResult`, R-U serialization and overflow lists; `design/gdd/networking-channel-contract.md` CCR-3; `design/gdd/networking-message-criticality.md` MCR-2
**Requirement**: `TR-hd-005` (placeholder)

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: messages travel in the project envelope with explicit codecs; R-U maps to the `Reliable` channel.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure codec)
**Engine Notes**: none — `Span<byte>` / `BinaryPrimitives`, as the other codecs.

**Control Manifest Rules (this layer)**:
- Required: explicit little-endian codec, no reflection serializers
- Required: every message type has one `MessageRoutingRegistry` row
- Forbidden: allocation in encode/decode

---

## Acceptance Criteria

*From the GDDs above, scoped to this story:*

- [ ] `HitCheckResult` readonly struct in `src/Foundation/Networking/WireProtocol/`: `AttackerEntityId`, `TargetEntityId`, `Result` (`HitResult`, a byte on the wire; the enum is in `src/Foundation/Networking/HitResult.cs`, Story 001), `ServerTick` (uint); `BodySize = 13`, `BatchSize = 17`.
- [ ] `MessageTypeId = 0x0306`.
- [ ] Sub-message write and read in `BatchSubMessageCodec`: field order attacker, target, result, tick, little-endian.
- [ ] `MessageRoutingRegistry` row: R-U, server to client, body 13, rationale from MCR-2; the architecture routing-table test passes.
- [ ] `RUBatchCategory.HitCheckResult` added. `RUBatchWriter` writes it after `SkillCooldownUpdate` and before `LootBidUpdate`, and drops it after `LootBidUpdate` and before `SkillCooldownUpdate`.

---

## Implementation Notes

- Follow `EntityHealthUpdate` for the struct and `BatchSubMessageCodec` entries.
- `RUBatchWriter` has a `WriteOrder`-style table and `DropOrder` (`RUBatchWriter.cs:134–146`); adding the enum value changes numeric values and array indexing. Check every use of the enum, including the existing writer tests.
- An out-of-range `Result` byte: decide at `/story-readiness` (the wire rule elsewhere is substitute, not drop).
- Add the new type to the ADR-012 allow-list (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`) if the boundary test asks for it.

---

## Out of Scope

- Story 003: the per-tick step that sends the batch
- Story 004: creating the message after `CheckHit`
- Client handling of the message

---

## QA Test Cases

- **Round trip**: attacker 7, target 51, result `MissFacing`, tick 1000 → written then read, all fields equal.
- **Byte layout**: written bytes match the expected little-endian layout and the 17-byte batch size.
- **Truncated input**: fewer bytes than the sub-message → read returns false.
- **Routing row**: the registry has exactly one row for `0x0306`, R-U, server to client.
- **Serialization order**: a batch holding `SkillCooldownUpdate`, `HitCheckResult` and `LootBidUpdate` writes them in that order.
- **Overflow order**: in an overflowing batch, `LootBidUpdate` drops first, then `HitCheckResult`, then `SkillCooldownUpdate`.
- **No allocation**: write and read inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/HitCheckResult_Codec_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Story 003, Story 004
