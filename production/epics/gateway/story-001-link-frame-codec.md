# Story 001: Link frame codec

> **Epic**: Gateway
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — "Where routing runs (ADR-016)" (the zone link carries `RegisterZone`, `PlayerCount`, `ZoneDraining`)
**Requirement**: `TR-auth-003` (link part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3 link format, Decision 6 shared code)
**ADR Decision Summary**: One loopback TCP connection per zone. Frame = `uint32` length (LE) + `byte` message type + payload; every request carries a `uint requestId` echoed by its reply. The codec lives in `src/ServerLogic/Gateway/Shared/`, compiled by Unity and by `gateway/IronGrind.Shared/` (`netstandard2.1`, C# 9).

**Engine**: Unity 6.3 LTS + .NET 10 (shared source) | **Risk**: LOW
**Engine Notes**: Shared code must compile under both Unity's C# 9 and `netstandard2.1`: no `Span`-only APIs missing from netstandard2.1, no records with `init` beyond C# 9, `BinaryPrimitives` is available in both.

**Performance**: no tick impact — encoding and decoding run on the gateway's link I/O and the zone's link thread (Story 010); the tick only reads decoded structs. One allocation per decoded message is acceptable.

**Control Manifest Rules (this layer)**:
- Required: frame = `uint32` LE length + `byte` type + payload; `requestId` echoed — ADR-016 Decision 3
- Required: shared source in `src/ServerLogic/Gateway/Shared/` with no `.csproj` in the folder, compiled by `gateway/IronGrind.Shared/` — ADR-016 Decision 6
- Forbidden: server-only DLL references (Npgsql, Dapper) from `src/ServerLogic/Gateway/Shared/` — ADR-012 / ADR-016

---

## Acceptance Criteria

- [ ] `GatewayLinkMessageType` byte enum with exactly these 10 values (decided at readiness 2026-11-09; the high bit marks gateway → zone) and bodies (little-endian; `AccountID`, `CharacterID`, `ZoneID` as their raw `uint32`):

  | Type | Message | Body | Bytes |
  |---|---|---|---|
  | 0x01 | `RegisterZone` | zoneId u32, templateId u8, udpPort u16, capacity u16, liveCount u16, accountId u32 × liveCount | 11 + 4n |
  | 0x02 | `PlayerCount` | count u16 | 2 |
  | 0x03 | `ZoneDraining` | empty | 0 |
  | 0x04 | `SessionReleased` | accountId u32 | 4 |
  | 0x05 | `AdmitResult` | requestId u32, accepted u8 (0/1), reason u8 (`AdmitRejectReason`; `None` if and only if accepted) | 6 |
  | 0x06 | `EvictResult` | requestId u32 | 4 |
  | 0x07 | `ReconnectResult` | requestId u32, accepted u8 (0/1), newToken[16] (all zeros when rejected) | 21 |
  | 0x81 | `Admit` | requestId u32, accountId u32, characterId u32, token[16] | 28 |
  | 0x82 | `Evict` | requestId u32, accountId u32 | 8 |
  | 0x83 | `Reconnect` | requestId u32, accountId u32, token[16] | 24 |

- [ ] Each side decodes only the other side's messages: the zone decoder accepts 0x81–0x83, the gateway decoder 0x01–0x07; a type from the wrong direction, an `accepted` byte other than 0/1, `accepted = 1` with a reason other than `None` (or `0` with `None`), or a non-zero `newToken` on a rejected `ReconnectResult` is a protocol violation
- [ ] Every message round-trips (encode → decode equal) for normal and boundary values (0, max, empty `liveAccounts`, max-length `liveAccounts` that still fits the frame cap)
- [ ] `TryReadFrame` on a byte buffer: returns "need more bytes" for a partial header or body, one frame for an exact buffer, and leaves trailing bytes for the next frame
- [ ] A declared length above `GATEWAY_LINK_MAX_FRAME_BYTES` (65 536, passed in, not hardcoded) or of 0 is reported as a protocol violation (caller closes the link); so is an unknown message type or a body whose length does not match its type
- [ ] Decoding never throws on malformed input; tokens are always exactly 16 bytes
- [ ] The same test cases pass in both runners, as two files (precedent: Authentication Story 002's `Authentication_BodyCodec_tests.cs` / `Auth_BodyCodec_tests.cs`): `tests/EditMode/Gateway/GatewayLink_Codec_tests.cs` (Unity) and `gateway/IronGrind.Gateway.Tests/GatewayLink_Codec_tests.cs`
- [ ] `AdmitRejectReason` moved from `src/ServerLogic/Gateway/` to `src/ServerLogic/Gateway/Shared/` (with its `.meta`), namespace `IronGrind.ServerLogic.Gateway` and values `None=0, NotActive=1, Full=2, InvalidToken=3` unchanged; the assembly-boundary tests still pass

---

## Implementation Notes

*Derived from ADR-016 Decisions 3 and 6:*

- Folder: `src/ServerLogic/Gateway/Shared/Link/`, namespace `IronGrind.ServerLogic.Gateway.Link` for new code. (`AccountID.cs` in the same shared folder keeps its old namespace `IronGrind.CharacterPersistence` because it was moved — not a convention for new code.) Add `.meta` files (Unity) — the gateway csproj already globs `src/ServerLogic/Gateway/Shared/**/*.cs`.
- `AccountID`, `CharacterID`, `ZoneID` are already shared types; use them in the decoded structs.
- `AdmitResult.reason` is `AdmitRejectReason`, moved into the shared folder by this story (moved types keep their namespace — ADR-016 Decision 6); run the assembly-boundary tests (`tests/EditMode/Architecture/`) after the move.
- The type values and field widths are not in ADR-016 (it lists field names only); this story is their source of truth. `RegisterZone.liveCount` is bounded by the frame cap: (65 536 − 1 − 11) / 4 = 16 381 accounts.
- The length field counts type byte + payload (state it in the doc comment; both sides must agree).
- No sockets here: pure byte encode/decode, so the gateway listener (Story 003) and the zone link thread (Story 010) share it.

---

## Out of Scope

- Story 003 / 010: sockets, threads, link lifecycle
- Story 004: request/reply correlation and timeouts

---

## QA Test Cases

- **AC-3**: Round trip
  - Given: each of the 10 message types with representative and boundary field values
  - When: encoded into a frame, then decoded
  - Then: decoded fields equal the originals; frame length field = 1 + payload length
  - Edge cases: `liveAccounts` empty; `liveAccounts` at the largest count under the frame cap; all-zero token; `requestId` = `uint.MaxValue`
- **AC-4**: Partial frames
  - Given: a valid frame split at every byte offset
  - When: fed incrementally
  - Then: "need more" until the last byte, then exactly one frame; two concatenated frames yield two frames in order
- **AC-5/6**: Violations
  - Given: length 0, length = cap + 1, unknown type byte, `PlayerCount` with 3-byte body, `Admit` with a 15-byte token
  - Then: protocol violation reported, no exception, no partial message returned
- **AC-2**: Direction and flag checks
  - Given: the zone decoder fed `PlayerCount` (0x02); the gateway decoder fed `Admit` (0x81); `AdmitResult` with `accepted = 2`; `AdmitResult(accepted = 1, reason = Full)`; `AdmitResult(accepted = 0, reason = None)`; rejected `ReconnectResult` with a non-zero token
  - Then: each is a protocol violation
- **AC-1**: Sizes
  - Given: each message encoded
  - Then: body length equals the table (`RegisterZone` with n accounts = 11 + 4n)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Gateway/GatewayLink_Codec_tests.cs` and `gateway/IronGrind.Gateway.Tests/GatewayLink_Codec_tests.cs` — must pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (Authentication Story 001's shared folders exist)
- Unlocks: Story 003, Story 004, Story 010
