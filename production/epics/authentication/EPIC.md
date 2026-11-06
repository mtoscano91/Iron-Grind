# Epic: Authentication

> **Layer**: Core
> **GDD**: design/gdd/authentication.md
> **Architecture Module**: Authentication
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories authentication`

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

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/authentication.md` are verified
- Logic stories (AccountID type, credential validation) have passing test files in `tests/EditMode/Authentication/`
- Gateway stories have passing tests in `gateway/IronGrind.Gateway.Tests/` (ADR-016 Decision 6)
- Integration stories (client → Caddy → gateway → zone) have tests or documented playtest evidence in `production/qa/evidence/`
- ADR-016's Validation Criteria and Engine Compatibility verification items pass (resolved 2026-11-06: the sidecar/IPC decision is ADR-016)

## Next Step

Run `/create-stories authentication` to break this epic into implementable stories. The architecture gap is closed (ADR-016); stories that need the .NET 10 SDK or Caddy wait for ADR-016 Migration Plan step 4.
