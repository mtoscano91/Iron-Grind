# Epic: Authentication

> **Layer**: Core
> **GDD**: design/gdd/authentication.md
> **Architecture Module**: Authentication
> **Status**: Ready
> **Stories**: 17 created 2026-11-06 (`/create-stories authentication`, lean) — see Stories below

## Overview

Authentication is the server-side identity verification layer that gates all player access to Project Iron Grind. When a client connects, Authentication validates the player's credentials, binds the verified account to a `CharacterID`, and issues a session token that all subsequent server operations reference. No character state is loaded, no zone is entered, and no server-authoritative game operation is permitted until Authentication succeeds. Authentication runs entirely in the server's connection-driven path — it fires before the tick loop and before zone assignment, and has no presence in the 20 Hz tick loop.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-016: Gateway and Authentication Process (Accepted 2026-11-06) | Authentication runs in the .NET 10 gateway (`gateway/IronGrind.Gateway/`): HTTPS `/v1/login` and `/v1/register` through Caddy, Argon2id (Konscious) behind `MAX_AUTH_PENDING`, accounts table (migration 004), account → zone map; the zone keeps its `SessionTokenStore`, filled by `Admit` (`AdmitToken`); shared code in `src/Foundation/Shared/` and `src/ServerLogic/Gateway/Shared/` | MEDIUM — `UnityWebRequest` on iOS, link thread under IL2CPP |
| ADR-004: Networking — NGO Substrate | The zone side (`SessionHandshake` with CharacterID + token) uses the Networking Core envelope; the login itself is HTTPS, not NGO | LOW (consumer only, not owner) |
| ADR-006: Persistence Layer (+ note 2026-11-06) | `accounts` table, sequences and the `gateway` role (ADR-016 Decision 5) | LOW |
| ADR-013: Server Random Provider | Session tokens and salts from `RandomNumberGenerator` | LOW |

*Resolved 2026-11-06:* the untraced sidecar decision this epic was created with (CR-AUTH-4: standalone .NET sidecar over named pipes, no ADR) is replaced by ADR-016. `auth-sidecar-ipc.md` is superseded. Gateway code and its tests live in `gateway/` (`dotnet test gateway/IronGrind.Gateway.sln`), outside the Unity test runner; zone-side code lives in `src/ServerLogic/Gateway/`, client code in `src/Client/Auth/`.

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-auth-001 | Full authentication = credential verification + AccountID→CharacterID resolution + session token issuance, all three complete before any game operation is permitted (CR-AUTH-1) | ❌ No ADR (design-only, LOW risk) |
| TR-auth-002 | `AccountID` is a `readonly struct` wrapping `uint`; `AccountID(0)` reserved/invalid; monotonic server-side counter, never reused, persisted independently from `CharacterID`'s counter (CR-AUTH-2/3) | ❌ No ADR (design-only, LOW risk) |
| TR-auth-003 | Authentication runs outside the Unity IL2CPP binary and off the zone tick, in the .NET gateway process (ADR-016): HTTPS through Caddy for clients, loopback TCP link to zones; no sidecar, no named pipes (CR-AUTH-4) — revised 2026-11-06 | ✅ ADR-016 Decisions 1–3 |
| TR-auth-004 | Password hashing via Argon2id (`Konscious.Security.Cryptography`), minimum `iterations=2, memorySize=19456KB, parallelism=1`, PHC string storage, 16-byte salt; bcrypt explicitly rejected (72-byte truncation defect) (CR-AUTH-5) | ✅ ADR-016 Decision 1 |
| TR-auth-005 | Client sends plaintext credentials over TLS only; server hashes on receipt; client MUST NOT pre-hash; server MUST reject non-TLS connections (CR-AUTH-6) | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: TR-auth-001 to 005 are registered in `docs/architecture/tr-registry.yaml` (TR-auth-003 revised 2026-11-06 for ADR-016).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Shared code folders and `IronGrind.Shared` | Logic | Complete | ADR-016, ADR-012 |
| 002 | Auth body codec | Logic | Ready | ADR-016 |
| 003 | Migration 004 and the `gateway` database role | Integration | Ready | ADR-016, ADR-006 |
| 004 | Gateway packages, config and the Argon2id hasher | Logic | Ready | ADR-016, ADR-013 |
| 005 | Credential validation and the lockout formula | Logic | Ready | ADR-016 |
| 006 | Account repository — registration | Integration | Ready | ADR-016, ADR-006 |
| 007 | Account repository — login state | Integration | Ready | ADR-016 |
| 008 | Registration service | Logic | Ready | ADR-016 |
| 009 | Login — credential path | Logic | Ready | ADR-016 |
| 010 | Login — session establishment | Logic | Ready | ADR-016, ADR-013 |
| 011 | Login — token path | Logic | Ready | ADR-016 |
| 012 | HTTP endpoints and the gateway host | Integration | Ready | ADR-016 |
| 013 | Zone admits a gateway token | Logic | Ready | ADR-016, ADR-011 |
| 014 | Zone `Evict` and `Reconnect` handlers | Integration | Ready | ADR-016, ADR-011 |
| 015 | Client HTTPS auth client | Integration | Ready | ADR-016 |
| 016 | Caddy TLS-only front | Integration | Ready | ADR-016, ADR-007 |
| 017 | End-to-end login → zone entry | Integration | Ready | ADR-016 |

Work in order; each story's `Depends on:` says what must be Done first. Story 017 also waits for the gateway epic's link, map and router stories (epic not yet created — ADR-016 Migration Plan step 7). Story 005 has no TR-ID yet (`TR-auth-???`: CR-AUTH-7/13, F-AUTH-1) — register at the next `/architecture-review`. AC-AUTH-24 (map memory, advisory) belongs to the gateway epic.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/authentication.md` are verified
- Logic stories (AccountID type, credential validation) have passing test files in `tests/EditMode/Authentication/`
- Gateway stories have passing tests in `gateway/IronGrind.Gateway.Tests/` (ADR-016 Decision 6)
- Integration stories (client → Caddy → gateway → zone) have tests or documented playtest evidence in `production/qa/evidence/`
- ADR-016's Validation Criteria and Engine Compatibility verification items pass (resolved 2026-11-06: the sidecar/IPC decision is ADR-016)

## Next Step

Story 001 Complete (2026-11-06; ADR-016 Migration Plan step 6 done). Next: `/story-readiness production/epics/authentication/story-002-auth-body-codec.md` (Story 004 is also unblocked; Story 003 = step 5).
