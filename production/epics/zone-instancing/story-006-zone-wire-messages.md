# Story 006: Zone Wire Messages and Codecs

> **Epic**: Zone Instancing
> **Status**: Complete — 2026-10-23 (`/story-done`, COMPLETE WITH NOTES); readiness 2026-10-23: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core (schemas in `IronGrind.Foundation`, shared with the client)
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 6 hours (confirmed at `/story-readiness` 2026-10-23)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-7, CR-ZI-8 step 6, CR-ZI-9; `design/gdd/networking-wire-protocol.md` — Zone Instancing Messages, `PlayerJoinedZone`, `ZoneSessionEnded`, Bulk-Transfer Messages (`EntityState`, `ZoneStateSnapshotFragment`); `networking-message-criticality.md`; `networking-channel-contract.md`
**Requirement**: `TR-zi-006`, `TR-zi-008` *(epic placeholders)*

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: game messages travel as NGO unnamed custom messages inside the project envelope; schemas are plain codecs.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (NGO 2.13.3) — limited here: codecs are plain byte encoders and do not touch NGO
**Engine Notes**: none for the codecs.

**Control Manifest Rules (this layer)**:
- Required: a new message type needs a row in `MessageRoutingRegistry` before its schema is accepted (MCR-2)
- Required: wire schemas shared with the client live in `IronGrind.Foundation`; nothing server-only in them — ADR-012
- Guardrail: encode / decode without per-call allocation where the existing codecs avoid it

---

## Acceptance Criteria

- [ ] Schema class + codec + `MessageRoutingRegistry` row for each of the five messages. MessageTypeIDs are provisional (no GDD or ADR assigns them; see TD-011) — say so in each doc comment. All rows: `NetworkChannel.ReliableOrdered`, `MessageDeliveryContext.PriorityPath`, `isMcr3Exception: false`.

  | Message | Id | Body | Pillar | Direction |
  |---|---|---|---|---|
  | `ZoneFullResponse` — `ZoneID zoneId`, `JoinRejectedReason reason`, `byte retryAfterSeconds` | `0xE130` | 6 B | Infrastructure | `ServerToOwningClient` |
  | `ZoneSnapshotRequest` — `ZoneID zoneId`, `uint snapshotVersion` | `0xE131` | 8 B | Infrastructure | `ClientToServer` |
  | `ZoneSessionEnded` — `ZoneID zoneId`, `DisconnectReason reason`, `int gracePeriodSeconds` | `0xE132` | 9 B | Infrastructure | `ServerToOwningClient` |
  | `PlayerJoinedZone` — `EntityID entityId`, `characterName` (ushort length + UTF-8, ≤ 24 B), `int level`, `short posX/Y/Z` | `0xE133` | 16–40 B | SocialSignals (Pillar 3) | `ServerToAllZoneClients` — own zone only |
  | `ZoneStateSnapshotFragment` — `uint snapshotVersion`, `ushort totalFragments`, `ushort fragmentIndex`, payload ≤ `MAX_MESSAGE_BODY_BYTES − 8` (504 B) | `0xF000` (bulk range `0xF000–0xFFFF`) | 8 + payload | Infrastructure | `ServerToOwningClient` |

  `EntityState` is a payload element inside the fragment, not a message: it gets a schema and codec but no MessageTypeID and no registry row.
- [ ] `EntityState` codec (tagged union, `entityType` always first; layout under Implementation Notes): player body 53 B with an empty name and 77 B with a 24-byte name; mob body 35 B. The read method returns the number of bytes consumed so a caller can walk a packed payload.
- [ ] `enum JoinRejectedReason : byte { ZoneFull = 0, ZoneDoesNotExist = 1, LoadCharacterFailed = 2, Other = 255 }` and `enum EntityType : byte { Player = 0, Mob = 1 }` in `IronGrind.Networking` (Foundation, next to `DisconnectReason`). Unknown bytes per CR-NET-7.4 and the GDD enum notes:
  - `JoinRejectedReason`: substitute `Other` and log an anomaly — new `WireEnumCodec.DecodeJoinRejectedReason(byte, ushort messageTypeId)` following the existing decoders.
  - `DisconnectReason`: substitute `Other` via the existing `WireEnumCodec.DecodeDisconnectReason`.
  - `EntityType`: the `EntityState` read fails (`false`). Entries carry no length prefix, so the GDD's "skip to the next entry" is not possible — the caller stops parsing that payload.
- [ ] Round-trip tests for every message and for `EntityState` (player with empty name, player with 24-byte name, mob); each body length equals the size listed.
- [ ] Malformed input is rejected through the codec convention (`TryReadBody` returns `false`, nothing thrown): truncated buffer for every message; name length over 24 bytes; unknown `entityType`; `totalFragments == 0`; `fragmentIndex ≥ totalFragments`; fragment payload over 504 bytes. Fixed-size bodies use the existing `body.Length < BodySize` check (trailing bytes tolerated, as in `SetTargetCodec`).
- [ ] Encode side: a name over 24 UTF-8 bytes is truncated at a character boundary (the `ServerBroadcast_Enhancement9Codec` pattern); a fragment payload over 504 bytes throws `ArgumentException` (the `InventorySlotUpdateCodec` pattern).
- [ ] Registry test: each of the five ids is classified with the pillar, channel, direction and delivery context in the table above.

---

## Implementation Notes

- Follow the existing codec pattern in `src/Foundation/Networking/WireProtocol/` (for example `InventoryFullSyncCodec`, `WireFixedPointCodec`, `WireIdCodec`).
- **Schemas store raw wire values** (`short` positions ×100, `short` rotation ×32,767, `ushort` cycle timer 0–10,000, `short` facing angle ×10), following the `EntityPositionUpdate` precedent. Converting to and from fixed point (`WireFixedPointCodec`) is the caller's job (Story 007 on the server, the client on receipt).
- Little-endian throughout (`BinaryPrimitives`, as the existing codecs). `bool` is one byte, written `0`/`1`; any non-zero byte reads `true`.
- **`EntityState` layout** (`networking-wire-protocol.md`, Bulk-Transfer Messages):
  - Shared header, 33 B: `EntityType entityType` (1), `EntityID entityId` (4), `short posX, posY, posZ` (6), `short rotX, rotY, rotZ, rotW` (8), `int currentHP` (4), `int maxHP` (4), `bool isInCombat` (1), `bool isGhost` (1, always false for mobs), `ushort cycleTimer` (2, mobs send 0 without a cycle), `short facingAngle` (2).
  - Player fields, 20–44 B: `string characterName` (ushort length + UTF-8, ≤ 24 B), `int level` (4), `int currentMP` (4), `int maxMP` (4), `byte equipmentAppearanceFlags` (1), `bool isInDeadState` (1), `uint respawnTicksRemaining` (4).
  - Mob fields, 2 B: `ushort mobTypeId`.
- **Cap exemption and transport fragmentation are not codec concerns:** the fragment's cap exemption is the `isCapExempt` flag at enqueue time, and ADR-004's fragmented reliable delivery belongs to the sender (Stories 007 / 014).
- **GDD follow-ups (advisory, not blocking):** `ZoneFullResponse` and `ZoneSnapshotRequest` have no rows in `networking-message-criticality.md` / `networking-channel-contract.md`; `networking-wire-protocol.md` reassembly step 3 says "length-prefixed EntityState deserialization" and the `EntityType` enum note says "skip to the next entry", but `EntityState` has no length prefix.
- **Not here (user decision 2026-10-22):** `SessionHandshake` and `SessionReady` codecs — their schemas are pending OQ-NC-SER-2. Stories 009 / 010 reach them through seams; the codecs go to whoever closes OQ-NC-SER-2. `PlayerLeftZone` belongs to the exit flow of the session contract and is not needed by this epic's stories.

**Decisions at `/story-readiness` 2026-10-23 (user approved)**

1. MessageTypeIDs: `0xE130`–`0xE133` for the four non-bulk messages (free block after Inventory's `0xE120`/`0xE121`), `0xF000` for the fragment. Provisional, like every existing id (TD-011).

---

## Out of Scope

- Story 007: building a snapshot and splitting it into fragments
- Client-side reassembly (`ZoneSnapshotReassemblyTracker` exists in Foundation)

---

## QA Test Cases

- **Round trip** — each message encodes then decodes to equal fields; body length equals the spec.
- **EntityState sizes** — player, empty name → 53; player, 24-byte name → 77; mob → 35; first byte is `entityType`; bytes consumed equals body size.
- **Packed payload** — a player entry followed by a mob entry decode in sequence using the bytes-consumed result.
- **Malformed** — 25-byte name length, `entityType = 2`, `totalFragments = 0`, `fragmentIndex = totalFragments`, payload of 505 bytes, truncated buffer for each message → `false`, no exception.
- **Enum substitution** — `JoinRejectedReason` byte 3 → `Other`; `DisconnectReason` byte 4 → `Other`; both log an anomaly.
- **Encode guards** — a 30-byte (and a multi-byte UTF-8) name is truncated at a character boundary to ≤ 24 bytes; a 505-byte fragment payload throws `ArgumentException`.
- **Registry** — each new id is classified with the pillar, channel, direction and delivery context in the table above.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_WireMessages_tests.cs` — must exist and pass.

**Status**: [x] Created and passing (95 runs)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 007, 008, 009, 010, 012, 014

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 7/7 passing, all by automated test. None deferred.
**Deviations** (advisory, notes only — not logged as tech debt):
1. Extra API: `EntityStateCodec.GetBodySize` / `PlayerJoinedZoneCodec.GetBodySize`; `WriteBody` slices to the exact encoded size (added at code review so Story 007 can pack entries tightly).
2. Stricter encode: the fragment encoder also throws on `totalFragments == 0` / `fragmentIndex >= totalFragments`; `EntityID.Invalid` throws via `WireIdCodec` (an `EntityState` entry reports against `0xF000`).
3. Not enforced in the codecs (stated in doc comments): decode accepts `EntityID` 0 — the consumer must reject it; encode has no `ZoneID.Invalid` guard.
4. Invalid UTF-8 in a name decodes to U+FFFD rather than failing (GDD silent; pinned by a test).
5. New constant `ZoneStateSnapshotFragment.MaxMessageBodyBytes = 512` (no CR-NET-7.6 constant existed in Foundation).
6. GDD follow-ups: `ZoneFullResponse` / `ZoneSnapshotRequest` lack rows in `networking-message-criticality.md` and `networking-channel-contract.md`; `networking-wire-protocol.md` says "length-prefixed EntityState" (reassembly step 3) and "skip to the next entry" (EntityType note), but `EntityState` has no length prefix.
7. `TR-zi-006` / `TR-zi-008` remain placeholders until `/architecture-review`.
**Scope**: within the story plus `WireStringCodec.cs` (internal helper) and `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (15 ADR-012 allow-list entries, required).
**Notes for other stories**:
- Story 007: size each entry with `EntityStateCodec.GetBodySize`, serialize the whole snapshot first, then `totalFragments = ceil(bytes / 504)`; reject `EntityID.Invalid` from decoded entries on the client; stop walking a payload when `TryReadBody` returns false (`bytesConsumed` is 0).
- Tests: a lone surrogate cannot pass through a `[TestCase]` string argument (attribute metadata is UTF-8; it arrives as three U+FFFD) — build such strings in the test body.
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_WireMessages_tests.cs` (95 runs).
**Code Review**: Complete — CHANGES REQUIRED, all 10 applied (`a5d4ba1`).
