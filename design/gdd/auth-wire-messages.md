# Authentication Wire Messages — Protocol Primitive

> **Status**: Approved 2026-11-04 (aligned with ADR-016: HTTPS bodies, `LoginResponse`, `classType`; `characterName` + character-name errors added after the authentication.md lean re-review the same day). Before: Draft (2026-05-29)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-11-04
> **Type**: Protocol primitive — referenced by authentication.md, networking-session-token.md and zone-instancing.md

## Purpose

Canonical source for all client↔gateway message schemas in the authentication flow. Resolved the naming conflict between `authentication.md` and `networking-session-token.md` CR-TOK-3/CR-TOK-4, and the AccountID-on-wire contradiction (2026-05-21). Re-targeted on 2026-11-04 to the gateway transport of ADR-016.

**Canonical name**: `ConnectionRequest`. *(Previously `LoginRequest` in authentication.md — renamed 2026-05-21.)*

---

## Transport (ADR-016)

These are **HTTPS request and response bodies**, not UDP game messages. The client talks to the gateway through Caddy on port 443 (TLS); the gateway listens on loopback behind it.

| Endpoint | Request body | Response body |
|---|---|---|
| `POST /v1/login` | `ConnectionRequest` | `LoginResponse` |
| `POST /v1/register` | `RegistrationRequest` | `RegistrationResult` |

- `Content-Type: application/octet-stream`.
- **Every outcome, including failure, returns HTTP 200** with the result in the body. A non-200 status, a transport error, or the client's `LOGIN_HTTP_TIMEOUT_SECONDS` elapsing means transport or server failure, and the client treats it as `ServerError`.
- Field encoding follows `networking-wire-protocol.md` field rules: little-endian integers, `bool` as one byte (0/1), strings as length-prefixed UTF-8. There is no message-type ID or channel header — the endpoint identifies the body.
- The schemas and their codec live in shared code (`src/Foundation/Shared/`, compiled by both Unity and the gateway — ADR-016 Decision 6), so client and gateway use one implementation.
- The client never uses plain HTTP and never ships a certificate handler that accepts every certificate (ADR-016 Decision 2).

---

## Message Schemas

### ConnectionRequest (client → gateway, `POST /v1/login`)

The single client-initiated message for both fresh login and token-based reconnect.

```
ConnectionRequest {
  username:   string     // UTF-8; normalized to lowercase by the gateway; max USERNAME_MAX_LENGTH (24) chars post-normalization
  password:   string     // UTF-8 plaintext; empty ("") on token path — NOT null
  tokenField: byte[16]   // all-zeros = fresh login; non-zero = reconnect token
}
```

**Path dispatch (gateway):**

| `tokenField` | Path | Notes |
|---|---|---|
| All-zeros `byte[16]{0}` | Fresh credential path (authentication.md CR-AUTH-9, CR-AUTH-11) | `username` + `password` evaluated |
| Non-zero | Token path (networking-session-token.md CR-TOK-4) | `password` ignored; `username` used to resolve `AccountID`; the gateway sends `Reconnect` to the zone holding the session, which validates and rotates the token |

**AccountID on the token path**: the gateway normalizes `username`, looks up the account record and takes `AccountID` from it. `AccountID` is NEVER transmitted to clients — the client does not store or send it (`authentication.md` CR-AUTH-2).

---

### RegistrationRequest (client → gateway, `POST /v1/register`)

```
RegistrationRequest {
  username:      string   // UTF-8; max USERNAME_MAX_LENGTH (24) chars; private login name, never shown in game
  password:      string   // UTF-8 plaintext
  characterName: string   // ASCII [A-Za-z0-9_], max CHARACTER_NAME_MAX_LENGTH (16); the visible name (authentication.md CR-AUTH-13)
  classType:     byte     // ClassType of the new character (class-system.md); unknown value → ClassInvalid
}
```

`classType` and `characterName` added 2026-11-04. The character name is chosen separately from the username and must not equal it (`authentication.md` CR-AUTH-13).

---

### LoginResponse (gateway → client)

Answers every `POST /v1/login`, on both paths and for both outcomes. Replaces the earlier pair `ConnectionAccepted` + `LoginResult` (ADR-016 Decision 2).

```
LoginResponse {
  success:        bool               // true = authenticated and admitted to a zone
  errorCode:      AuthFailureReason  // None=0 on success; reason on failure
  // Present only when success = true:
  sessionToken:   byte[16]           // per CR-TOK-1; already stored by the target zone (Admit) or rotated by it (Reconnect)
  characterId:    uint               // resolved CharacterID; CharacterID(0) never sent
  zoneId:         uint               // target ZoneID per CR-ZI-6 (fresh login) or the zone holding the session (token path); 0 never sent
  zoneServerPort: ushort             // UDP port of that zone server (ADR-007)
  // AccountID is NOT present — server-internal only (authentication.md CR-AUTH-2)
}
```

`AuthFailureReason : byte { None=0, InvalidCredentials=1, ServerError=255 }`

Body length: failure = 2 bytes; success = 2 + 16 + 4 + 4 + 2 = **28 bytes**.

| Outcome | Body | Client action |
|---|---|---|
| Fresh login success | `success=true`, token, characterId, zoneId, port | Connect to the zone on `zoneServerPort` and send `SessionHandshake(zoneId, characterId, token)` within `CONNECTING_TIMEOUT_SECONDS` |
| Token path success | Same fields; `sessionToken` is the rotated token (CR-TOK-5) | Replace the stored token; reconnect to that zone |
| Credential failure (any cause in CR-AUTH-9) | `success=false, errorCode=InvalidCredentials` | Show the generic message; no hint about the cause |
| Infrastructure failure (EC-AUTH-5, EC-AUTH-10, `MAX_AUTH_PENDING` full) | `success=false, errorCode=ServerError` | Show the server-error message |

**Zone routing:** `zoneId` is chosen by fill-first routing (zone-instancing.md CR-ZI-6), run by the gateway, and the zone has accepted `Admit` for this token before the response is written. If routing fails (no zone accepts `Admit` after one re-route), the response is `ServerError`.

**Zone no longer Active at handshake (formerly `RoutingRedirectMessage`):** a zone that leaves Active rejects new `Admit`s and drops admitted entries whose client has not arrived, sending `SessionReleased` to the gateway. A `SessionHandshake` that arrives after that gets `ZoneFullResponse(ZoneDoesNotExist)` (zone-instancing.md EC-ZI-1), and the client returns to login. There is no server-initiated redirect.

---

### RegistrationResult (gateway → client)

```
RegistrationResult {
  success:   bool
  errorCode: RegistrationError   // valid only when success = false
}
```

`RegistrationError : byte { UsernameTaken=0, UsernameTooShort=1, UsernameTooLong=2, UsernameInvalidChars=3, PasswordTooShort=4, PasswordTooLong=5, ClassInvalid=6, CharacterNameTaken=7, CharacterNameTooShort=8, CharacterNameTooLong=9, CharacterNameInvalidChars=10, CharacterNameMatchesUsername=11, ServerError=255 }`

`ClassInvalid` and the `CharacterName*` codes added 2026-11-04.

---

### Retired messages

| Message | Retired | Replaced by |
|---|---|---|
| `LoginResult` (S→C) | 2026-11-04 | `LoginResponse` fields `success`, `errorCode`, `zoneId` |
| `ConnectionAccepted` (S→C) | 2026-11-04 | `LoginResponse` fields `sessionToken`, `characterId`. The name was itself a 2026-05-21 rename of an S→C `SessionHandshake`; `SessionHandshake` remains only the C→S zone-entry request (`networking-channel-contract.md` CCR-2) |
| `RoutingRedirectMessage` (S→C) | 2026-11-04 | No replacement. Routing runs in the gateway, so a zone cannot re-route; the race ends in `ZoneFullResponse(ZoneDoesNotExist)` and a new login |

---

## Wire Encoding

Field-level encoding rules come from `networking-wire-protocol.md` (see Transport above). Because these bodies travel over HTTPS, they get no message-type IDs or delivery classes in `networking-wire-protocol.md`. Exact byte layouts are pinned by the shared codec and its tests in the first authentication story.

---

## Cross-Document Corrections — Applied

| Document | Rule | Issue | Status |
|---|---|---|---|
| `authentication.md` | CR-AUTH-9 and all rules | "LoginRequest" naming | ✅ Renamed to "ConnectionRequest" (2026-05-21) |
| `authentication.md` | All S→C auth success message references | "`SessionHandshake`" — CCR-2 conflict | ✅ Renamed to "`ConnectionAccepted`" (2026-05-21); now `LoginResponse` (2026-11-04) |
| `authentication.md` | CR-AUTH-11 step 3 | "SessionHandshake contains AccountID" | ✅ AccountID removed (2026-05-21); step rewritten for the gateway (2026-11-04) |
| `authentication.md` | AC-AUTH-21(b), AC-AUTH-30 | AccountID on the wire | ✅ Asserted absent from `LoginResponse` (2026-11-04) |
| `authentication.md` | CR-AUTH-8, CR-AUTH-13, AC-AUTH-34/35 | `classType`, `ClassInvalid`; separate `characterName` (name = username dropped after the lean re-review: public login names enabled lockout griefing) | ✅ Added 2026-11-04 |
| `networking-session-token.md` | CR-TOK-3/CR-TOK-4 | "AccountID is included in ConnectionRequest" | ✅ Updated (2026-05-20) |
| `networking-session-token.md` | CR-TOK-2, CR-TOK-5 step 3, EC-TOK, AC-TOK-1/5 | Token delivered in `SessionHandshake` / `ConnectionAccepted` | ✅ Now `LoginResponse` (2026-11-04) |
| `auth-wire-messages.md` | `LoginResult` | Unified success+failure result carrying `zoneId` (OQ-ZI-4, 2026-05-29) | ✅ Superseded by `LoginResponse` 2026-11-04 |
| `auth-wire-messages.md` | `RoutingRedirectMessage` | Added 2026-05-29 (OQ-ZI-4) | ✅ Retired 2026-11-04 — routing runs in the gateway |
| `zone-instancing.md` | CR-ZI-6, CR-ZI-8 step 1, EC-ZI-1, Interactions, OQ-ZI-4 | Routing at "auth time" in the zone server; redirect | ✅ Updated 2026-11-04 for the gateway |
