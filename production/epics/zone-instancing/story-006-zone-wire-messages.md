# Story 006: Zone Wire Messages and Codecs

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core (schemas in `IronGrind.Foundation`, shared with the client)
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

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

- [ ] Schema class + codec + `MessageRoutingRegistry` row for each fully specified message:
  - `ZoneFullResponse` — `ZoneID zoneId`, `JoinRejectedReason reason`, `byte retryAfterSeconds`; 6-byte body; R-OD, S→C
  - `ZoneSnapshotRequest` — `ZoneID zoneId`, `uint snapshotVersion`; 8-byte body; R-OD, C→S
  - `ZoneSessionEnded` — `ZoneID zoneId`, `DisconnectReason reason`, `int gracePeriodSeconds`; 9-byte body; R-OD, S→C
  - `PlayerJoinedZone` — `EntityID`, `characterName` (ushort length + UTF-8, ≤ 24 bytes), `int level`, `short posX/Y/Z`; ≤ 40-byte body; R-OD, S→all zone clients
  - `EntityState` — tagged union: 33-byte shared header, player fields (53–77 bytes total), mob fields (35 bytes total); `entityType` first
  - `ZoneStateSnapshotFragment` — `uint snapshotVersion`, `ushort totalFragments`, `ushort fragmentIndex`, payload ≤ `MAX_MESSAGE_BODY_BYTES − 8` (504); MessageTypeID in `0xF000–0xFFFF`; bulk, cap-exempt
- [ ] `enum JoinRejectedReason : byte { ZoneFull = 0, ZoneDoesNotExist = 1, LoadCharacterFailed = 2, Other = 255 }`; `enum EntityType : byte { Player = 0, Mob = 1 }`; decoders range-check enums (CR-NET-7.4).
- [ ] Round-trip tests for every message; exact body sizes as listed; `EntityState` player body 53 bytes with an empty name and 77 with a 24-byte name; mob body 35.
- [ ] Decoding rejects malformed input (short buffer, name over 24 bytes, unknown `entityType`, `fragmentIndex ≥ totalFragments`) without throwing past the codec's existing error convention.

---

## Implementation Notes

- Follow the existing codec pattern in `src/Foundation/Networking/WireProtocol/` (for example `InventoryFullSyncCodec`, `WireFixedPointCodec`, `WireIdCodec`).
- Positions use the CR-NET-7.2 fixed-point codec (×100); rotation quaternion ×32,767.
- **Not here (user decision 2026-10-22):** `SessionHandshake` and `SessionReady` codecs — their schemas are pending OQ-NC-SER-2. Stories 009 / 010 reach them through seams; the codecs go to whoever closes OQ-NC-SER-2. `PlayerLeftZone` belongs to the exit flow of the session contract and is not needed by this epic's stories.

**Open decisions for `/story-readiness`**

1. MessageTypeID values for the five non-bulk messages (the GDDs assign none) and the bulk id inside `0xF000–0xFFFF`.

---

## Out of Scope

- Story 007: building a snapshot and splitting it into fragments
- Client-side reassembly (`ZoneSnapshotReassemblyTracker` exists in Foundation)

---

## QA Test Cases

- **Round trip** — each message encodes then decodes to equal fields; body length equals the spec.
- **EntityState sizes** — player, empty name → 53; player, 24-byte name → 77; mob → 35; first byte is `entityType`.
- **Malformed** — 25-byte name, `entityType = 2`, `fragmentIndex = totalFragments`, truncated buffer → rejected.
- **Registry** — each new id is classified with the channel and direction above.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_WireMessages_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 007, 008, 009, 010, 012, 014
