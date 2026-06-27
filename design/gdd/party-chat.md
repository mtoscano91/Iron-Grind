# Party Chat

> **Status**: Approved (lean re-review 2026-06-19 — one-line Dependencies fix applied)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-19
> **Implements Pillar**: Social Gravity (primary)

## Overview

Party Chat is the real-time text messaging system that connects party members within a session. It routes player-typed messages through the server, which validates membership via the Party System and delivers each message to all eligible party members over the reliable-ordered channel. Party Chat is purely event-driven — it carries no server tick budget and does not participate in the 20 Hz loop. At MVP, Party Chat is scoped to party-only delivery; world chat (zone-wide broadcast) ships in Vertical Slice.

For the player, Party Chat is where the grind gets a voice. A "gg" after a rare drop, a healer typing "low mp" before the next pull, a tank calling "go" before charging the elite mob — these words are the human layer of Social Gravity that the party HUD's HP bars cannot carry. Where the party panel shows who is alive and at what health, chat carries intent, banter, and the coordination that turns four individual grinders into a group. The party feels mechanical without it; it feels alive with it.

## Player Fantasy

The party panel is mechanical. It shows HP bars draining, levels, the leader crown. What it cannot show is what it *means* when that Steel sword drops, or when the enhancement holds at +7 on the third attempt, or when the tank dies pulling too many and takes the party down with them. Party Chat is where those moments land first — before the zone sees it, before the server broadcasts it, the four people who were standing there already know. The witnesses are in that small window, reacting in real time. A line of text is the fastest way the game has to carry the weight of a moment, and in Iron Grind the heavy moments are the only ones that matter.

Underneath the peaks is the texture that makes them land: the offhand line between pulls, the "gg" that costs nothing and means something, the two words a healer types at 40% MP that reroute the next three minutes of play. Nobody is performing. The party chat during a long grind session is not a highlight reel — it is proof that a person is in the trench beside you, putting in the same hours, noticing the same things. Without it, the party panel is four health bars and a timer. With it, the party feels like a crew.

*Pillar alignment: Social Gravity (primary) — chat is the voice of the party that makes grouping feel human, not just efficient. Legendary Gear (supporting) — the big moments of the prestige economy (rare drops, successful high-enhancement, devastating losses) need a room where they register as social events, and chat is that room before the world knows.*

## Detailed Design

### Core Rules

**CR-PC-1 — Message Send Flow**

All party chat messages follow a server-authoritative relay pipeline. No client-to-client delivery path exists.

**Client optimistic display:** On submit, the client renders the sender's own message immediately in a *pending* state (visually distinct — e.g. dimmed or with a sending indicator). The pending state resolves to *confirmed* when the server echo `PartyChatMessage` arrives, or to *failed* (error-state in place, no silent disappearance) if `PartyChatRejected` or `PartyChatThrottled` is received. The server remains the authoritative source: the message is not considered delivered until the echo arrives. Per-recipient deliver guarantee is R-OD per-connection only — Party Chat accepts per-member delivery divergence at reconnect boundaries.

Server relay steps:
1. Resolve sender `CharacterID` from the authenticated `SenderEntityID ↔ CharacterID` session mapping. The primary lookup key is the connection's authenticated `clientId`; `SenderEntityID` from the client payload is cross-checked against the result. If no match, log a `ChatSpoofAttempt` anomaly and silently discard.
2. Run content validation (CR-PC-3). On failure, send `PartyChatRejected` with the applicable reason code and halt.
3. Call `IPartySystem.GetPartyID(CharacterID)`. If the result is a solo party (`IsSoloParty = true`), silently discard — no response to sender.
4. Check spam throttle (CR-PC-4). If the rate limit is exceeded, send `PartyChatThrottled` to sender and halt.
5. Call `IPartySystem.GetPartyMembersWithStatus(PartyID)` → `PartyMemberSlot[]` (see party-system.md Exposed Data Types). Locate the sender's slot by matching `CharacterID`. If the sender's `MemberStatus` is not `Online` or `Dead`, send `PartyChatRejected{SenderNotEligible}` and halt.
6. Apply CR-PC-2 eligibility to each slot to build the delivery set. Include `Online` and `Dead` members; skip `Ghost`, `OutOfZone`, and empty slots (`CharacterID == 0`).
7. Populate `PartyChatMessage` using the server-authoritative character name from the session registry (never from the client payload). Resolve each eligible recipient's NGO `clientId` at dispatch time (not cached from step 5) to narrow the reconnect race window. Dispatch on R-OD to each eligible recipient including the sender echo. Per-recipient send failures must not abort dispatch to remaining recipients — each `CustomMessagingManager.Send` call is independent. Advance sender's sliding-window counter.

Wire schemas (body-only; prepend 10-byte CR-NET-7.1 envelope; add 4-byte `SenderEntityID` for C→S):

```
PartyChatRequest (R-OD, C→S):
{
    ushort textByteCount;  // 2 bytes — UTF-8 byte length; must be in [1, PARTY_CHAT_MAX_BYTES]
    byte[] textBytes;      // N bytes — UTF-8 encoded text; max PARTY_CHAT_MAX_BYTES (384) bytes
                           // NOT the standard string type (CR-NET-7.2 caps at 255 bytes)
}
// Standalone: 10B (envelope) + 4B (SenderEntityID) + 2B + N = 16B min, 400B max

PartyChatMessage (R-OD, S→PARTY):
{
    EntityID senderEntityId; // 4 bytes — sender's zone entity; used for click-to-inspect in UI
                             // Note: if sender disconnected between relay and delivery, this EntityID
                             // may reference a de-spawned entity — click-to-inspect must no-op silently
    string   senderName;     // ushort(2) + UTF-8 max 24 bytes = 26B max
                             // Server-sourced from session registry — never echoed from client
    ushort   textByteCount;  // 2 bytes
    byte[]   textBytes;      // N bytes — validated copy of request textBytes; max 384 bytes
}
// Standalone: 10B + 4B + 26B + 2B + N = 43B practical min (1B text floor), 426B max
// (min uses max name for capacity planning; true structural min is 20B with 1-char name + 1-char text)

PartyChatRejected (R-OD, S→C):
{
    byte rejectedReason; // 1 byte — PartyChatRejectedReason : byte
}
// Standalone: 11B

// PartyChatRejectedReason enum : byte
//   InvalidEncoding   = 0  malformed UTF-8 byte sequence (CR-PC-3 check 1)
//   InvalidContent    = 1  control characters (U+0000–U+001F, U+007F) or null bytes (CR-PC-3 check 2)
//   MessageEmpty      = 2  zero codepoints after validation (CR-PC-3 check 3)
//   MessageTooLong    = 3  exceeds PARTY_CHAT_MAX_CODEPOINTS (128) codepoints (CR-PC-3 check 4)
//   SenderNotEligible = 4  sender MemberStatus not Online or Dead (CR-PC-1 step 5, after party membership resolved)

PartyChatThrottled (R-OD, S→C):
{
    // Empty body — 10-byte envelope only
}
// Standalone: 10B
// Client: show "sending too quickly" toast; re-enable input after window resets
```

*Cross-document flags: (1) The `textBytes` field exceeds the standard `string` type's 255-byte cap — document as a distinct `chatText` wire type in `networking-wire-protocol.md` CR-NET-7.2. (2) `PartyChatRequest`, `PartyChatMessage`, `PartyChatRejected`, and `PartyChatThrottled` must be added to the CCR-3 routing table in `networking-channel-contract.md` and registered as a new "Party Chat Messages" section in `networking-wire-protocol.md`.*

---

**CR-PC-2 — Recipient Eligibility**

Eligibility is evaluated at relay time (step 6 of CR-PC-1).

| MemberStatus | Receives delivery | May send |
|---|---|---|
| `Online` | Yes | Yes |
| `Dead` | Yes | Yes — connected and in zone during respawn countdown |
| `Ghost` | No | No — disconnected; no active connection |
| `OutOfZone` | No | No — different zone instance |

The sender is subject to the same eligibility rule. If all other members are ineligible, the delivery set is a singleton (sender echo only) — no suppression. Empty slots (`CharacterID == 0`) are always skipped.

---

**CR-PC-3 — Message Validation**

Checks run in order; the first failure determines the `PartyChatRejectedReason`. Subsequent checks are not evaluated.

| Order | Check | Rejection reason |
|---|---|---|
| 1 | `textBytes` is valid UTF-8 | `InvalidEncoding` |
| 2 | Decoded text contains no Unicode Cc control characters (U+0000–U+001F, U+007F) and no embedded null bytes | `InvalidContent` |
| 3 | Decoded text contains at least 1 Unicode codepoint | `MessageEmpty` |
| 4 | Decoded text contains at most `PARTY_CHAT_MAX_CODEPOINTS` (128) Unicode codepoints | `MessageTooLong` |

*Note: Sender eligibility (`SenderNotEligible`) is checked at CR-PC-1 step 5 (after party membership is resolved), not here — MemberStatus is sourced from `GetPartyMembersWithStatus` where the PartyID is available.*

Check 4 counts codepoints, not bytes. One CJK character counts as 1 regardless of UTF-8 byte width. Messages exceeding the limit are rejected in full — no server-side truncation. No semantic content filtering at MVP (CR-PC-6).

---

**CR-PC-4 — Spam Throttle**

The server maintains a per-`CharacterID` **true sliding-window** rate counter:
- `PARTY_CHAT_RATE_MAX = 5` messages per window
- `PARTY_CHAT_RATE_WINDOW_SECONDS = 10` seconds

**Algorithm (true sliding window):** Store a ring buffer of up to `PARTY_CHAT_RATE_MAX` server timestamps per `CharacterID`. Before relaying, count the number of timestamps in the buffer that fall within the past `PARTY_CHAT_RATE_WINDOW_SECONDS`. If that count equals `PARTY_CHAT_RATE_MAX`, send `PartyChatThrottled` and discard. On successful relay, append the current timestamp to the ring buffer, evicting the oldest if full. This prevents the boundary-burst exploit that fixed-window counters allow (RATE_MAX msgs at end of window A + RATE_MAX msgs at start of window B = 2× RATE_MAX in ~0ms).

**Storage:** `Dictionary<CharacterID, CircularBuffer<ServerTimestamp>>`, owned by the Party Chat server component. Counter state is ephemeral — server restart resets all windows; reconnect within the same window does NOT reset the counter.

If dispatching would exceed the rate, the server sends `PartyChatThrottled` and discards — no relay.

Token accounting: one token consumed per successfully relayed `PartyChatMessage`. Messages rejected by CR-PC-3 or the solo-party discard rule (step 3) do not consume a token. The counter persists across Ghost/reconnect events within the same window — reconnecting does not reset the rate limit.

---

**CR-PC-5 — Chat History and Reconnect**

The server maintains no per-party message buffer. Once dispatched, messages are not retained server-side.

On reconnect after a Ghost session, the party chat window initializes empty. No historical messages are replayed. The client may display a local-only system line (not a server message): *"Reconnected to party chat."*

Members who reconnect within the 5-second Ghost grace period (CR-PS-11) were never marked Ghost and never missed delivery — no replay needed.

---

**CR-PC-6 — Content Filtering**

No semantic content filtering (profanity detection, keyword blocklist) applies at MVP. Only structural validation applies (CR-PC-3 checks 2–4). Party chat's closed 2–4 person scope substantially reduces toxicity surface versus zone-wide channels. Moderation is deferred post-MVP and will require its own GDD section.

---

### States and Transitions

Party Chat is stateless at the system level — no per-party or per-member chat state machine. The only maintained state is the per-sender sliding-window counter (CR-PC-4), which is an ephemeral server-side counter, not a named system state.

---

### Interactions with Other Systems

| System | Direction | Interface | Trigger |
|---|---|---|---|
| **Party System** | Party Chat → calls | `IPartySystem.GetPartyID(CharacterID)` → `PartyID` | Step 3: resolve sender's party; detect solo-discard |
| **Party System** | Party Chat → calls | `IPartySystem.GetPartyMembersWithStatus(PartyID)` → `PartyMemberSlot[]` (see party-system.md Exposed Data Types) | Step 5: enumerate recipient candidates; `MemberStatus` drives sender SenderNotEligible check and CR-PC-2 filter |
| **Networking Core** — session registry | Party Chat ← reads | `clientId ↔ (EntityID, CharacterID, CharacterName)` mapping | Step 1: validate `SenderEntityID`; step 7: resolve NGO `clientId` per recipient + populate `senderName` |
| **Networking Core** — `CustomMessagingManager` | Party Chat → calls | `CustomMessagingManager.Send(ulong clientId, payload, ReliableSequenced)` | Step 7: one call per eligible recipient including sender echo |

*Party Chat does not interface with Zone Instancing (reads `OutOfZone` status from `PartyMemberSlot`), Death & Respawn (reads `Dead` status from `PartyMemberSlot`), or Character Persistence (character name resolved from in-memory session registry at relay time).*

## Formulas

**F-PC-1 — Peak Incoming Chat Bandwidth**

```
BW_peak = (N_party × R_max × B_msg) / W_s
```

| Variable | Symbol | Type | Range | Description |
|---|---|---|---|---|
| Party size | `N_party` | int | [1, 4] | Active sending members (worst case = MAX_PARTY_SIZE = 4) |
| Max messages/window | `R_max` | int | [1, 5] | PARTY_CHAT_RATE_MAX |
| Message size | `B_msg` | int | [43, 426] bytes | `PartyChatMessage` wire size (10B envelope + 4B entityId + 26B name max + 2B byteCount + N text bytes; 43B capacity-planning floor uses name-max; true structural min is 20B with 1-char name + 1-char text) |
| Window duration | `W_s` | int | [5, 30] | PARTY_CHAT_RATE_WINDOW_SECONDS (seconds); default 10 |
| Peak bandwidth | `BW_peak` | float | [0, 852] bytes/s | Sustained peak incoming chat rate at one party member |

**Output Range:** 0 (no activity) to 852 bytes/s (all 4 members sending at max rate with max-length messages). The ceiling is structural — it cannot be exceeded without violating CR-PC-4. Total server egress = N_party × BW_peak = 4 × 852 = 3,408 bytes/s — use the total for server capacity planning; the per-client figure (852 bytes/s) applies to client receive budgets.

**Example (worst case):** `N_party=4`, `R_max=5`, `B_msg=426`, `W_s=10` → `(4 × 5 × 426) / 10 = 852 bytes/s ≈ 6.8 Kbps`. At 20 Hz: ~42.6 bytes/tick via R-OD standalone. Negligible relative to the R-U tick batch (F-NET-6). Use this formula to verify that tuning `PARTY_CHAT_RATE_MAX` or `PARTY_CHAT_RATE_WINDOW_SECONDS` does not introduce unintended bandwidth amplification.

*The spam throttle (CR-PC-4) is a sliding-window queue predicate, not a formula — fully specified in Detailed Design. Sustainable send rate at default settings: `PARTY_CHAT_RATE_MAX / PARTY_CHAT_RATE_WINDOW_SECONDS = 5 / 10 = 0.5 msg/s` per sender (one message every 2 seconds at steady state).*

## Edge Cases

- **EC-PC-1 — Sender disconnects between CR-PC-1 step 2 (validation) and step 7 (dispatch):** Relay to all other eligible recipients completes normally. The sender echo dispatch at step 7 fails silently — the socket is closed and no `PartyChatRejected` is generated. Message is not re-sent.

- **EC-PC-2 — A recipient transitions to Ghost between step 5 (GetPartyMembers snapshot) and step 7 (dispatch):** The step 6 filter uses the step 5 snapshot; the member was Online in the snapshot and remains in the delivery set. `CustomMessagingManager.Send` on the now-closed clientId fails silently. All other eligible recipients are unaffected — per-recipient send failures do not abort the relay.

- **EC-PC-3 — Party disbands between step 3 (GetPartyID) and step 5 (GetPartyMembers):** Step 3 resolves a valid non-solo PartyID; by step 5 the party has disbanded. `GetPartyMembers` returns null or an empty array. Party Chat treats either as an empty delivery set — no `PartyChatMessage` dispatched, no `PartyChatRejected` sent. The message is silently dropped (validation was clean; no rejection code covers a disband race).

- **EC-PC-4 — All non-sender party members are ineligible (Ghost or OutOfZone) at relay time:** The delivery set is a singleton {sender}. Per CR-PC-2, the server still dispatches the echo and advances the throttle counter. No suppression occurs — the sender echo is indistinguishable from a successful multi-member relay, confirming the message passed all validation.

- **EC-PC-5 — Two `PartyChatRequests` from the same sender arrive in the same server tick with the throttle counter at `RATE_MAX − 1`:** Requests are processed in NGO receive-queue arrival order within the tick. Request A is evaluated at count=4 → relay succeeds, counter reaches 5. Request B is evaluated at count=5 → `PartyChatThrottled` sent, no relay. Arrival order determines which request consumes the last token; neither is lost or reordered.

- **EC-PC-6 — A member's zone-transition status change fires in the same tick as a `PartyChatRequest`:** If the sender's transition fires before CR-PC-1 step 2 within the tick processing order, their MemberStatus is `OutOfZone` → `SenderNotEligible` rejection. If after step 2, validation passes and relay proceeds (echo dispatch to the departing connection may fail silently per EC-PC-1). For a transitioning recipient, the same ordering applies: before step 5 → excluded from delivery set; after step 5 → dispatch may fail silently. Both orderings produce correct outcomes without special handling.

- **EC-PC-7 — CharacterName is absent or empty in the session registry at step 7:** Server logs a `ChatSenderNameMissing` anomaly and substitutes the literal string `"[Unknown]"` in the `senderName` field. Message is dispatched normally to all eligible recipients — delivery is not suppressed.

- **EC-PC-8 — `PartyChatMessage` and `PartyChatThrottled` are both due to the same client in the same tick:** Both are independent R-OD dispatches via `CustomMessagingManager.Send` on the same clientId. There is no per-tick R-OD dispatch cap that suppresses them. Client receives both messages; FIFO ordering on the R-OD channel is preserved.

- **EC-PC-9 — Messages dispatched during a sub-5-second disconnect gap (CR-PS-11 grace window):** Ghost state is suppressed for the disconnecting member, but NGO R-OD delivery is scoped to the active connection object. Any `PartyChatMessages` dispatched to the old connection during the brief gap are not buffered for the new connection on reconnect. The chat window initializes empty on reconnect per CR-PC-5. This silent gap is bounded by the 5-second grace period and is the intended behavior.

- **EC-PC-10 — `textByteCount` in `PartyChatRequest` does not match wire payload at the deserialization layer:** The server validates `textByteCount` against the actual remaining bytes in the received packet at deserialization, before CR-PC-3 runs. Three cases are caught and all result in a `ChatMalformedPayload` log and silent discard with no `PartyChatRejected` sent (no CR-PC-3 reason code covers wire-format violations):
  1. `textByteCount > PARTY_CHAT_MAX_BYTES (384)`: oversized declared length — server does not allocate or read beyond 384 bytes.
  2. `textByteCount > (received_message_total_bytes − sizeof(ushort))`: short/truncated packet — declared length exceeds remaining buffer; server must not attempt the read (buffer overread risk).
  3. `textByteCount < (received_message_total_bytes − sizeof(ushort))`: padded packet — declared length is less than remaining buffer; server reads only `textByteCount` bytes and discards the packet.
  
  **4-byte codepoint note:** The 384-byte cap covers all BMP Unicode (max 3 bytes/codepoint, 128 × 3 = 384). Supplementary codepoints (emoji, rare Han extensions, 4 bytes each) are limited to 96 per message at the byte cap, not 128. A message of exactly 97 emoji (388 bytes) is discarded at deserialization. The client UI must enforce both limits independently: send button disabled if `codepoints > PARTY_CHAT_MAX_CODEPOINTS OR UTF8ByteCount(input) > PARTY_CHAT_MAX_BYTES`.

- **EC-PC-11 — Party disbands while sender has the keyboard open and text typed:** When `IsSoloParty` transitions to `true` (party disband), any text in the input field is discarded. The client renders a local system line in the chat window (not a server message): *"Party has disbanded."* The chat panel then closes, following the same animation as the party HUD disband sequence. The in-progress `PartyChatRequest` is never sent. No `PartyChatRejected` is generated.

## Dependencies

**Upstream (Party Chat depends on):**

| System | Status | What this system needs from it |
|---|---|---|
| **Networking Core** | Approved | Server-authoritative relay pipeline (CR-NET-1); `CustomMessagingManager` for R-OD message dispatch (ADR-004); 10-byte message envelope (CR-NET-7.1); `clientId ↔ (EntityID, CharacterID, CharacterName)` session registry; `SESSION_TTL_SECONDS = 300` (Ghost reconnect window context) |
| **Party System** | Approved | `IPartySystem.GetPartyID(CharacterID)` → `PartyID`; `IPartySystem.GetPartyMembersWithStatus(PartyID)` → `PartyMemberSlot[]`; `MemberStatus` enum (Online, Dead, Ghost, OutOfZone); `IsSoloParty` flag; CR-PS-11 Ghost grace period (5s) defines the gap boundary for EC-PC-9 |

**Downstream (systems that depend on Party Chat):**

| System | Status | What it needs from Party Chat |
|---|---|---|
| **HUD** | Not Started | `PartyChatMessage` delivery triggers chat window rendering; `PartyChatRejected` / `PartyChatThrottled` drive client UI feedback (error toast, throttle indicator) |

**Bidirectional consistency check:**
- Networking Core → Party Chat not yet listed as downstream dependent in `networking-core.md` (authored before this GDD existed — must add after this GDD is approved) ⚠
- Party System → lists "Party Chat — Not Started — Party membership for chat message routing" in its downstream table ✓
- HUD → no GDD exists; must reference Party Chat when authored

**Cross-document amendments required before implementation:**
1. Add `PartyChatRequest`, `PartyChatMessage`, `PartyChatRejected`, `PartyChatThrottled` to `networking-wire-protocol.md` (new Party Chat Messages section)
2. Add those four messages to `networking-channel-contract.md` CCR-3 routing table
3. Define `chatText` wire type (ushort + UTF-8 bytes, max PARTY_CHAT_MAX_BYTES = 384) in `networking-wire-protocol.md` CR-NET-7.2

## Tuning Knobs

| ID | Constant | Default | Safe Range | Effect if Too Low | Effect if Too High |
|---|---|---|---|---|---|
| TK-PC-1 | `PARTY_CHAT_MAX_CODEPOINTS` | 128 | [32, floor(TK-PC-2 / 3)] | Messages feel truncated; tactical callouts get cut off | Longer wire payload; at 4 bytes/codepoint, exceeds `PARTY_CHAT_MAX_BYTES` ceiling before hitting codepoint limit. **At default TK-PC-2=384B: max safe TK-PC-1 = 128** (no headroom above default). Only raise TK-PC-1 above 128 if TK-PC-2 is raised proportionally (TK-PC-2 ≥ TK-PC-1 × 3 for BMP-only; TK-PC-1 × 4 for full Unicode safety). |
| TK-PC-2 | `PARTY_CHAT_MAX_BYTES` | 384 | [128, 512] | BMP codepoint limit effectively shrinks below 128; 4-byte characters even more restricted | Approaches 512-byte body budget (CR-NET-7.6); max `PartyChatRequest` standalone at 512B body = 528B, over budget. Also determines TK-PC-1 ceiling: TK-PC-1 ≤ floor(TK-PC-2 / 3). |
| TK-PC-3 | `PARTY_CHAT_RATE_MAX` | 5 | [2, 10] | Players can't react quickly to burst moments (rare drop, near-wipe) | Chat becomes spammable; 10 msgs/10s × 4 members = sustained 4× normal bandwidth per F-PC-1 |
| TK-PC-4 | `PARTY_CHAT_RATE_WINDOW_SECONDS` | 10 | [5, 30] | Shorter window = more aggressive per-second rate cap; fast typists hit throttle on short bursts | Longer window = more tokens accumulate; paired with high RATE_MAX allows extended sustained bursts |

**Interactions between knobs:**
- TK-PC-1 and TK-PC-2 are coupled: `PARTY_CHAT_MAX_BYTES` must always be `≥ PARTY_CHAT_MAX_CODEPOINTS × 3` to guarantee BMP coverage. Raising CODEPOINTS without raising BYTES silently reduces the effective codepoint limit for multi-byte characters.
- TK-PC-3 and TK-PC-4 together determine the sustainable send rate (`RATE_MAX / WINDOW_SECONDS`) and peak burst allowance. Validate any change against F-PC-1 to confirm `BW_peak` remains well within the R-OD path budget.

## Visual/Audio Requirements

Party Chat has no standalone VFX or animations. Requirements are limited to notification events:

| Event | Required Signal | Notes |
|---|---|---|
| `PartyChatMessage` received | Soft notification sound + chat panel highlight flash | Sound: brief, non-alarming; distinct from party invite and combat sounds. Flash: 0.3 s fade on the chat panel border or input area. Suppress sound while the input field is focused (player is actively typing). |
| `PartyChatThrottled` received | Shake animation on the send button + toast: "Sending too quickly" | No sound — throttle is a UX friction cue, not an error event. |
| `PartyChatRejected` received | Brief red-tinted flash on the input field + inline rejection reason string | Human-readable strings: "Message too long," "Invalid characters," "Empty message," etc. No sound. |

*Audio: specific sound file specs and mix category defined in the Audio System GDD when authored.*

## UI Requirements

The party chat UI is a single surface: a **persistent chat panel** embedded in the HUD, visible during active play.

**Chat panel:**
- **Safe area:** Panel left and bottom edges are anchored to `Screen.safeArea` bounds, not raw screen coordinates. Required for correct rendering on iPhone X+ notch/Dynamic Island in landscape. Do not use fixed pixel coordinates.
- Fixed position within safe area: bottom-center of the HUD, between the virtual joystick area (left) and the Combat UI skill buttons (right). The panel rect must not overlap the virtual joystick activation area. See HUD GDD (design/gdd/hud.md CR-HUD-16) for the authoritative layout position.
- Displays the last `CHAT_HISTORY_DISPLAY_COUNT` messages (client-side buffer only — server maintains no message log). Each line: `SenderName` (display column: maximum 96dp width; names that exceed this are truncated with "…") + ": " + message text. Wrap long messages within the panel width; no horizontal scroll.
- Auto-scrolls to newest message on each received `PartyChatMessage`. If the player has scrolled up to read earlier messages, auto-scroll is suppressed until they return to the bottom or send a message.
- **Combat opacity:** During active combat (local player's cycle timer running), the panel **background overlay** fades from 70% to 30% opacity after 4 seconds of no new messages. The text layer remains at **full opacity** against the dimmed backing at all times to maintain WCAG AA contrast. Fades back to 70% background immediately on a new `PartyChatMessage` or on player tap-on-panel (read-only; does not open input).
- All chat text must be legible at the minimum OS dynamic text size at arm's length. Minimum rendered font size: 12 pt.

**Input activation:**
- **The panel reading surface is passive.** Taps on the panel reading area scroll or restore combat opacity — they do not open the keyboard. This prevents accidental keyboard-open during Rhythm Mastery timing windows (joystick thumb drift into the chat panel region is a known mobile hazard).
- Input is opened exclusively via a dedicated **compose button**: a small affordance (minimum 44 dp × 44 dp) positioned at the edge of the panel furthest from the joystick dead zone (top-right corner of the panel). The compose button is always visible when the party panel is shown.
- Tapping the compose button activates the input field and raises the soft keyboard.

**Keyboard-active layout:**
- When the keyboard is raised, the panel shifts upward so its bottom edge sits 10dp above the keyboard top edge.
- In the keyboard-active state, the "must not overlap the virtual joystick activation area" constraint does not apply to the repositioned panel — the player is not using combat controls while typing. Specific HUD element overlap rules in the keyboard-active state are defined in the UX spec (`design/ux/party-chat-panel.md`).
- The shift is animated at the same duration as the iOS keyboard slide animation.

**Input field:**
- Send button (and keyboard Return) submits `PartyChatRequest`. Disabled while the input field is empty. Also disabled if `UTF8ByteCount(input) > PARTY_CHAT_MAX_BYTES` — the input field enforces both the codepoint limit (PARTY_CHAT_MAX_CODEPOINTS) and the byte limit (PARTY_CHAT_MAX_BYTES) independently; the send button disables on either violation.
- After submit: input field clears. **Keyboard remains open** to allow rapid follow-up messages. Player dismisses the keyboard explicitly (back gesture, tap outside panel, or cancel button). Panel returns to its default position only after keyboard is dismissed.
- A counter displays in the input field: `[codepoints]/128`. When UTF-8 byte count would exceed PARTY_CHAT_MAX_BYTES before codepoints reach the limit, the counter turns red and send is disabled.

**Hidden during solo play:** The chat panel is hidden when `IsSoloParty = true`. It becomes visible on joining a party and collapses when the party disbands.

📌 **UX Flag — Party Chat:** Run `/ux-design` to create a UX spec at `design/ux/party-chat-panel.md` before writing implementation epics. Stories referencing chat UI should cite the UX spec, not this GDD directly.

## Acceptance Criteria

All criteria must be verifiable by a QA tester without subjective judgement.

| ID | Criterion | Verify by |
|---|---|---|
| AC-PC-1 | Given two Online party members: sender submits a valid message → server delivers `PartyChatMessage` to both the sender (echo) and the recipient. Neither message appears in any other party's or non-party client's chat window. | Integration test — 2 clients, 1 party |
| AC-PC-2 | Sender submits a valid message: message appears in sender's chat window immediately (pending state) before the server echo arrives. When the `PartyChatMessage` echo arrives, the pending entry transitions to confirmed. If the server sends `PartyChatRejected` or `PartyChatThrottled` instead, the entry transitions to failed (error-state in place — it does not disappear). | Integration test — intercept client display events; verify pending→confirmed and pending→failed transitions |
| AC-PC-3a | A Dead party member (MemberStatus = Dead) receives `PartyChatMessage` delivery when a party member sends a valid message. | Integration test — 2 clients: 1 sender (Online), 1 receiver (Dead) |
| AC-PC-3b | A member who disconnected within the last 5 s (in the CR-PS-11 grace window): their MemberStatus is still `Online` (ghost suppressed during grace). Server attempts delivery; if the R-OD send to their last-known clientId fails at the socket level, the failure is silent (no `PartyChatRejected` to sender, no retry). | Integration test — disconnect client B; send message from A within 5 s; verify no PartyChatRejected to A |
| AC-PC-3c | A member with MemberStatus = Ghost (grace window elapsed; >5 s since disconnect): they are excluded by the CR-PC-2 eligibility filter (step 6). No delivery attempt is made. Sender receives no notification. | Integration test — let client B's grace window expire; verify server does not attempt R-OD to B |
| AC-PC-4 | A party with only 1 Online member and 0 Dead members: sender submits a message → receives only a sender echo. No secondary delivery is attempted. | Integration test — solo party member |
| AC-PC-5 | A message of exactly 0 bytes at the wire level causes `PartyChatRejected` with `reason = MessageEmpty` (2). | Unit test — server validation |
| AC-PC-6a | A `PartyChatRequest` wire payload where `textByteCount` field = 385 (exceeds `PARTY_CHAT_MAX_BYTES` = 384): server discards silently with no `PartyChatRejected` to sender; server logs `ChatMalformedPayload`. A payload with `textByteCount` = 384 and a valid 384-byte UTF-8 body is accepted and relayed. | Unit test — wire-level byte injection |
| AC-PC-6b | A valid `PartyChatRequest` payload containing exactly 129 ASCII characters (≤384 bytes, valid UTF-8, no control chars): server responds with `PartyChatRejected{reason = MessageTooLong (3)}` because 129 codepoints exceeds `PARTY_CHAT_MAX_CODEPOINTS` (128). A message of exactly 128 ASCII characters is accepted. | Unit test — server content validation |
| AC-PC-7 | A payload containing a non-UTF-8 byte sequence causes `PartyChatRejected` with `reason = InvalidEncoding` (0). | Unit test — server validation |
| AC-PC-8 | Sending exactly `PARTY_CHAT_RATE_MAX` (5) valid messages succeeds for all 5 (messages 1–5 relayed). A 6th message sent before T = 10 s from message 1 causes `PartyChatThrottled`. At T = 10 s from message 1 (sliding-window expiry reference point for message 1), the next message succeeds. | Integration test — rate limit harness; record server timestamps for messages 1 and 6 |
| AC-PC-9 | **Precondition:** Client B sends at least one message during the 5 s disconnection window while Client A is disconnected. Client A reconnects within 5 s: A's chat window shows no messages sent during the disconnection (messages are not replayed). | Integration test — reconnect within grace window |
| AC-PC-10 | A player with no party (solo) who sends `PartyChatRequest` receives no response. The message does not appear in the sender's UI. | Integration test — solo client |
| AC-PC-11 | Under worst-case load — 4 Online party members each sending 5 messages of 384 bytes (the maximum valid payload) within 10 s — measured server egress for Party Chat messages is ≤ 852 bytes/s (F-PC-1 ceiling). | Load test — bandwidth probe at F-PC-1 boundary |
| AC-PC-12 | Client A sends 3 valid messages (well within RATE_MAX), then disconnects and reconnects within the 5 s grace window. After reconnect, A can send exactly 2 more messages before the 6th causes `PartyChatThrottled` — confirming the sliding-window counter persisted across the reconnect. | Integration test — throttle state across reconnect |
| AC-PC-13 | A sender whose MemberStatus is OutOfZone submits a `PartyChatRequest`: server responds with `PartyChatRejected{reason = SenderNotEligible (4)}`. No `PartyChatMessage` is delivered to any party member. | Integration test — set sender MemberStatus to OutOfZone via party-system test hook |

## Open Questions

| ID | Question | Blocking? | Owner | Status |
|---|---|---|---|---|
| OQ-PC-1 | **Wire protocol amendments** — `PartyChatRequest`, `PartyChatMessage`, `PartyChatRejected`, and `PartyChatThrottled` must be registered in `networking-wire-protocol.md` (new "Party Chat Messages" section) and added to `networking-channel-contract.md` CCR-3 routing table. The `chatText` field type (ushort byte-count + UTF-8 bytes, max 384B) must be documented in CR-NET-7.2 alongside the standard `string` type. Required before any implementation epics are opened. | Yes — implementation blocker | Wire Protocol GDD author | OPEN |
| OQ-PC-2 | **networking-core.md downstream table** — Party Chat is not listed as a downstream dependent in `networking-core.md`, which was authored before Party Chat existed. A Party Chat row must be added to the Dependencies table before implementation. | No — documentation gap only | networking-core.md maintainer | OPEN |
| OQ-PC-3 | **Supplementary Unicode (emoji) cap** — The byte cap (PARTY_CHAT_MAX_BYTES = 384B) limits 4-byte codepoints to 96 per message, not the stated 128 (EC-PC-10). If emoji are a first-class player behavior, consider raising PARTY_CHAT_MAX_BYTES to 512 (128 × 4) and confirming the 528B request packet stays within per-message budget. Defer until playtesting reveals emoji usage patterns. | No — tuning concern | Game Designer | OPEN |
| OQ-PC-4 | **CHAT_HISTORY_DISPLAY_COUNT** — The UI Requirements reference a display buffer depth for the chat panel. Value TBD; typical MMORPG range is 8–20 lines. Low values lose context mid-pull; high values extend visual footprint. Define when the HUD GDD and UX spec are authored. | No — UI tuning | HUD GDD author | OPEN |
