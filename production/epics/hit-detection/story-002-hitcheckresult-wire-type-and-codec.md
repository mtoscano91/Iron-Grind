# Story 002: HitCheckResult Wire Type and Codec

> **Epic**: Hit Detection
> **Status**: Ready — `/story-readiness` 2026-10-30: NEEDS WORK (5 gaps) → fixed → Ready
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
- [ ] Sub-message `WriteHitCheckResult`, `WriteHitCheckResultBody` and `TryReadHitCheckResult` in `BatchSubMessageCodec`: field order attacker, target, result, tick, little-endian. `WriteHitCheckResult` calls the body writer, as `WriteEntityHealthUpdate` does.
- [ ] `WireEnumCodec.DecodeHitResult`: substitute-and-continue. An out-of-range byte logs an anomaly and returns `MissOutOfRange`, so a corrupt message cannot show a false hit. No `Enum.IsDefined` (forbidden on IL2CPP).
- [ ] `MessageRoutingRegistry` row: `pillars: RhythmMastery`, `channel: ReliableUnordered`, `direction: ServerToOwningClient`, `deliveryContext: ReliableUnorderedBatch`, `isMcr3Exception: false`, rationale from MCR-2; the architecture routing-table test passes. For monster attacks the defender counts as the owner of their own entity.
- [ ] `RUBatchCategory.HitCheckResult = 9` appended after `ConnectionQualityUpdate`, so existing values do not change. `RUBatchWriter` writes it after `SkillCooldownUpdate` and before `LootBidUpdate`, and drops it after `LootBidUpdate` and before `SkillCooldownUpdate`.
- [ ] `IronGrind.Networking.HitCheckResult` added to the Foundation list in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`.

---

## Implementation Notes

- Follow `EntityHealthUpdate` for the struct and `BatchSubMessageCodec` entries, and `DecodeDamageType` for the guarded decode.
- `RUBatchWriter` indexes per-category data by `(int)` category. Add the category to `WriteOrder` and `DropOrder`, and check how those per-category arrays are sized, since the highest value becomes 9.
- `HitCheckResult` is registered in `MessageRoutingRegistry`; check the routing-table test's discovered-type count still matches.

---

## Out of Scope

- Story 003: the per-tick step that sends the batch
- Story 004: creating the message after `CheckHit`
- Client handling of the message

---

## QA Test Cases

- **Round trip**: attacker 7, target 51, result `MissFacing`, tick 1000 → written then read, all fields equal.
- **Byte layout**: written bytes match the expected little-endian layout and the 17-byte batch size.
- **Body writer**: `WriteHitCheckResultBody` writes 13 bytes, matching the bytes after the header from `WriteHitCheckResult`.
- **Truncated input**: fewer bytes than the sub-message → read returns false.
- **Bad result byte**: bytes 3 and 255 → `MissOutOfRange`, one anomaly each, and the message is still read; 0, 1, 2 → their values with no anomaly.
- **Routing row**: the registry has exactly one row for `0x0306`, with the channel, direction and delivery context above.
- **Enum stability**: `ConnectionQualityUpdate` is still 8 and `HitCheckResult` is 9.
- **Opaque path**: a `PendingSubMessage` with `RUBatchCategory.HitCheckResult` and the body bytes goes through `RUBatchWriter.Write` and reads back equal.
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
