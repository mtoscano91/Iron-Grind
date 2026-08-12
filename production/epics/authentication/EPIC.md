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
| ADR-004: Networking — NGO Substrate | Authentication runs on the connection-driven path before `SessionHandshake`; consumes the Networking Core envelope for its own wire messages | LOW (consumer only, not owner) |

**⚠️ Untraced architecture decision**: CR-AUTH-4 specifies Authentication runs as a **standalone .NET sidecar process**, separate from the Unity IL2CPP headless game server binary, communicating via internal IPC (named pipes). This is a real architectural decision — process topology, IPC transport, concurrency cap (`MAX_AUTH_PENDING`), health-check contract — with **no Accepted ADR covering it**. The two GDDs that would fully specify this boundary, `design/gdd/auth-wire-messages.md` and `design/gdd/auth-sidecar-ipc.md`, are both still **Draft** (not Approved) per `systems-index.md`. Stories touching the sidecar/IPC boundary (CR-AUTH-4 through CR-AUTH-6, credential hashing, IPC transport) will be **Blocked** at `/story-readiness` until those primitives are Approved and this decision is either ADR'd or resolved as GDD-only (same treatment `/architecture-decision` gave ADR-004's own prerequisite gate). Stories scoped to the pure behavioral contract (CR-AUTH-1 through CR-AUTH-3: what "full authentication" means, `AccountID` type/generation) do not depend on the sidecar boundary and are not blocked by this gap.

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-auth-001 | Full authentication = credential verification + AccountID→CharacterID resolution + session token issuance, all three complete before any game operation is permitted (CR-AUTH-1) | ❌ No ADR (design-only, LOW risk) |
| TR-auth-002 | `AccountID` is a `readonly struct` wrapping `uint`; `AccountID(0)` reserved/invalid; monotonic server-side counter, never reused, persisted independently from `CharacterID`'s counter (CR-AUTH-2/3) | ❌ No ADR (design-only, LOW risk) |
| TR-auth-003 | Authentication runs as a standalone .NET sidecar process (not the Unity IL2CPP binary), communicating via internal IPC named pipes (CR-AUTH-4) | ❌ No ADR — see Untraced Architecture Decision above |
| TR-auth-004 | Password hashing via Argon2id (`Konscious.Security.Cryptography`), minimum `iterations=2, memorySize=19456KB, parallelism=1`, PHC string storage, 16-byte salt; bcrypt explicitly rejected (72-byte truncation defect) (CR-AUTH-5) | ❌ No ADR — see Untraced Architecture Decision above |
| TR-auth-005 | Client sends plaintext credentials over TLS only; server hashes on receipt; client MUST NOT pre-hash; server MUST reject non-TLS connections (CR-AUTH-6) | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/authentication.md` are verified
- Logic stories (AccountID type, credential validation) have passing test files in `tests/EditMode/Authentication/`
- Integration/sidecar stories have tests or documented playtest evidence in `production/qa/evidence/`
- The sidecar/IPC architecture decision (CR-AUTH-4) is resolved — either via `/architecture-decision` or by promoting `auth-wire-messages.md`/`auth-sidecar-ipc.md` from Draft to Approved — before any sidecar-boundary story is implemented

## Next Step

Run `/create-stories authentication` to break this epic into implementable stories. Expect the sidecar/IPC-touching stories to come back Blocked at `/story-readiness` until the architecture gap above is resolved.
