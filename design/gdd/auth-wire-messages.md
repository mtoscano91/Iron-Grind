# Authentication Wire Messages — Protocol Primitive

> **Status**: Draft
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-29
> **Type**: Protocol primitive — referenced by authentication.md and networking-session-token.md

## Purpose

Canonical source for all client↔server wire message schemas in the authentication flow. Resolved the naming conflict between `authentication.md` and `networking-session-token.md` CR-TOK-3/CR-TOK-4, and the AccountID-on-wire contradiction. All corrections applied to `authentication.md` on 2026-05-21.

**Canonical name**: `ConnectionRequest`. *(Previously `LoginRequest` in authentication.md — renamed 2026-05-21.)*

---

## Wire Message Schemas

### ConnectionRequest (C→S)

The single client-initiated message for both fresh connect and token-based reconnect.

```
ConnectionRequest {
  username:   string     // UTF-8; normalized to lowercase by server; max 24 chars post-normalization
  password:   string     // UTF-8 plaintext; empty ("") on token path — NOT null
  tokenField: byte[16]   // all-zeros = fresh connect; non-zero = reconnect token
}
```

**Path dispatch (server-side):**

| `tokenField` | Path | Notes |
|---|---|---|
| All-zeros `byte[16]{0}` | Fresh credential path (authentication.md CR-AUTH-9) | `username` + `password` evaluated |
| Non-zero | Token path (networking-session-token.md CR-TOK-4) | `password` ignored; `username` used to resolve `AccountID` |

**AccountID on the token path**: When `tokenField` is non-zero, the server:
1. Normalizes `username` to lowercase and looks up the account record (same DB call as the credential path).
2. Extracts `AccountID` from the account record.
3. Performs `ActiveSessionTokens[AccountID]` lookup per CR-TOK-4 step 1.

`AccountID` is NEVER transmitted to clients — the client does not store or send it. This preserves `authentication.md` CR-AUTH-2 ("AccountID is never transmitted to clients") unchanged.

> **Correction to `networking-session-token.md` CR-TOK-4 step 1**: The text "AccountID is included in `ConnectionRequest` as defined in `networking-wire-protocol.md`; it is present on both fresh-connect and reconnect paths" is incorrect. `AccountID` is derived server-side from the `username` field in `ConnectionRequest`; it is not a wire field. CR-TOK-4 step 1 must be revised to: *"Server derives `AccountID` by normalizing `username` from `ConnectionRequest` and looking up the account record. If found, performs `ActiveSessionTokens[AccountID]` lookup."*

---

### RegistrationRequest (C→S)

```
RegistrationRequest {
  username: string   // UTF-8; max 24 chars
  password: string   // UTF-8 plaintext
}
```

---

### LoginResult (S→C) — auth and zone routing result

Sent on both success and failure paths. Single message covering auth outcome and zone assignment.

```
LoginResult {
  success:   bool              // true = auth + zone routing succeeded; false = failed
  errorCode: AuthFailureReason // valid only when success = false; None=0 on success path
  zoneId:    uint              // valid only when success = true; server-assigned ZoneID per CR-ZI-6
                               // 0 = Invalid — never issued on the success path
}
```

`AuthFailureReason : byte { None=0, InvalidCredentials=1, ServerError=255 }`

**Message dispatch:**

| Outcome | Messages sent (server → client) | Notes |
|---|---|---|
| Auth success + zone assigned | `ConnectionAccepted(token, charId)` then `LoginResult(success=true, zoneId)` | Client MUST wait for `LoginResult` before sending `SessionHandshake`; `zoneId` is the zone target |
| Auth failure | `LoginResult(success=false, errorCode)` only | No `ConnectionAccepted` issued — no session established |

**Zone routing:** `zoneId` is determined by the fill-first routing algorithm (zone-instancing.md CR-ZI-6) during authentication — before the client sends `SessionHandshake`. If zone routing fails at auth time (e.g., all instances of the template fail init), `LoginResult(success=false, errorCode: ServerError=255)` is returned.

---

### RoutingRedirectMessage (S→C)

Sent when the zone assigned in `LoginResult` transitions out of Active before the client's `SessionHandshake` is processed. Gives the client a new zone assignment without requiring a full re-authentication.

```
RoutingRedirectMessage {
  newZoneId: uint   // newly assigned ZoneID per CR-ZI-6; always non-zero (ZoneID(0) is Invalid and must never be sent)
}
```

**Trigger:** Server receives `SessionHandshake` targeting a zone that is no longer Active (Draining, Closed, or failed to reach Active via T-3). Server re-runs zone routing (CR-ZI-6) and sends this message with the replacement zone. Supersedes EC-ZI-1's "wait for server redirect" (which was previously undefined).

**Client behaviour on receipt:** Client discards the prior `zoneId` from `LoginResult`, stores `newZoneId`, and resubmits `SessionHandshake(newZoneId)`. Normal retransmit limits (`MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS` from networking-session.md) apply to the redirected attempt.

**Edge case — redirect routing also fails:** If zone routing fails during redirect (e.g., all template instances are at hard cap and a new instance fails T-3 init), server sends `ZoneFullResponse(ZoneFull=0)` instead of `RoutingRedirectMessage`. Client must not retry automatically on `ZoneFull=0` — this signals a transient overload, not a routing error.

---

### RegistrationResult (S→C)

```
RegistrationResult {
  success:   bool
  errorCode: RegistrationError   // valid only when success = false
}
```

`RegistrationError : byte { UsernameTaken=0, UsernameTooShort=1, UsernameTooLong=2, UsernameInvalidChars=3, PasswordTooShort=4, PasswordTooLong=5, ServerError=255 }`

---

### ConnectionAccepted (S→C) — on Auth_Success or Reconnecting→Connected

> **Name change from `SessionHandshake`**: `authentication.md` referred to this message as `SessionHandshake`, but that name is reserved for the C→S zone-entry request in `networking-channel-contract.md` CCR-2, which explicitly states no S→C message of that name exists. The canonical S→C auth success message name is `ConnectionAccepted`. All references to `SessionHandshake` in S→C contexts in `authentication.md` must be updated to `ConnectionAccepted` in the revision pass (see cross-document corrections table).

Sent when the session is fully established.

```
ConnectionAccepted {
  sessionToken: byte[16]   // per CR-TOK-1; identifies this session for reconnect
  characterId:  uint       // resolved CharacterID; CharacterID(0) = error, must never appear
  // AccountID is NOT present — server-internal only (authentication.md CR-AUTH-2)
}
```

`AccountID` is used server-side to key `ActiveSessionTokens` (networking-session-token.md CR-TOK-7) and must never appear in the wire message. Character state fields (gold, level, HP, equipment) are in a follow-up `ZoneStateSnapshot` per `networking-session.md` CR-NET-6.4 — not in this message.

> **Correction to `authentication.md` CR-AUTH-11 step 3**: The text "Emit `SessionHandshake` containing session token, `AccountID`, and `CharacterID` to client" is incorrect on two counts: (1) this message must be named `ConnectionAccepted`, not `SessionHandshake` (CCR-2 name conflict — see section header note above); (2) `AccountID` is not a wire field. Correct CR-AUTH-11 step 3 to: *"Emit `ConnectionAccepted` containing session token and `CharacterID` to client."*

> **Correction to `authentication.md` AC-AUTH-21(b)**: The assertion "SessionHandshake carries token, AccountID, CharacterID" must be corrected to "`ConnectionAccepted` carries token and CharacterID only." This also resolves the contradiction with AC-AUTH-30.

---

## Wire Encoding

Byte-level encoding (field widths, message type IDs, framing format) is owned by `networking-wire-protocol.md`. The schemas above define semantic content only. These messages must be added to `networking-wire-protocol.md` as part of the authentication implementation sprint.

---

## Cross-Document Corrections — Applied

All corrections listed below were applied as of 2026-05-21.

| Document | Rule | Issue | Status |
|---|---|---|---|
| `authentication.md` | CR-AUTH-9 and all rules | "LoginRequest" naming | ✅ Renamed to "ConnectionRequest" (2026-05-21) |
| `authentication.md` | All S→C auth success message references | "`SessionHandshake`" — CCR-2 conflict | ✅ Renamed to "`ConnectionAccepted`" (2026-05-21) |
| `authentication.md` | CR-AUTH-11 step 3 | "SessionHandshake contains AccountID" | ✅ `ConnectionAccepted`; AccountID removed (2026-05-21) |
| `authentication.md` | AC-AUTH-21(b) | "SessionHandshake carries AccountID" | ✅ `ConnectionAccepted`; AccountID assertion removed (2026-05-21) |
| `networking-session-token.md` | CR-TOK-3/CR-TOK-4 | "AccountID is included in ConnectionRequest" | ✅ Updated (2026-05-20) |
| `networking-session-token.md` | CR-TOK-5 step 3 | "Delivered in the reconnect `SessionHandshake`" | ✅ Renamed to "`ConnectionAccepted`" (2026-05-20) |
| `auth-wire-messages.md` | `LoginResult` | Was documented as failure-only; now unified success+failure result carrying `zoneId` on success path (OQ-ZI-4) | ✅ Amended 2026-05-29 |
| `auth-wire-messages.md` | `RoutingRedirectMessage` | New S→C message — redirect client to new zone when assigned zone becomes unavailable before `SessionHandshake` processes (OQ-ZI-4) | ✅ Added 2026-05-29 |
| `zone-instancing.md` | Interactions table, OQ-ZI-4 note | "LoginResult must be amended to carry ZoneID" — now resolved; `zoneId` added to `LoginResult` success path | ✅ Resolved 2026-05-29 |
