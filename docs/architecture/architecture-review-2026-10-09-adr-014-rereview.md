# Architecture Review Report — ADR-014 (lean re-review)

> **Date:** 2026-10-09
> **Engine:** Unity 6.3 LTS (6000.3)
> **ADR reviewed:** ADR-014 Inbound Request Dispatch and Tick Order (Proposed, amended in `bb2064b`)
> **Mode:** `/architecture-review` on ADR-014 only — lean re-review after targeted fixes
> **Prior review:** `architecture-review-2026-10-09-adr-014.md` (CONCERNS: B1, B2, C1–C3, R1–R6)
> **Verdict:** **CONCERNS — ADR-014 stays Proposed.** Every item of the prior review that was due in the amendment is closed. The amended intake leaves one new item to fix before acceptance (N1) and four small items (S1–S4). The project-wide verdict stays **CONCERNS**.

## Scope Note

- **Read in full:** amended ADR-014, `architecture-review-2026-10-09-adr-014.md`.
- **Read in part:** `networking-wire-protocol.md` (envelope `SequenceNumber` row, CR-NET-7.5, CR-NET-7.10, the client→server schema headers, `SessionHandshake`, `ZoneSnapshotRequest`, `ClientBackgrounded`, `RttProbeEcho`, `SkillCastRequest`); `party-chat.md` (`PartyChatRequest` schema); `MessageRoutingRegistry.cs` (rows and `ValidateAndRoute`), `PendingSchemaDispatchException.cs`, `StaleDiscardComparer.cs`, `ServerTickLoop.cs` (`ServerTickNumber`), `ConnectionStateMachine.cs` (public members).
- **Not run:** the `unity-specialist` consultation (the amendment adds no engine claim beyond the reworded Verification Required 2 and 5); the full traceability matrix (Phases 2–3); `tr-registry.yaml` left as it is. The 26 rows of Decision 1 were not re-derived from the wire protocol one by one; the rows checked are listed below.

---

## Prior Findings

| # | Status | Evidence |
|---|---|---|
| B1 — stale check drops reliable requests | Closed | Decision 2 step 4 checks U-U types only and says why (CCR-1 shared counter). The highest-seen value advances for U-U messages only. The receive-order sentence and Verification Required 5 are restricted to R-OD. A test and a Validation Criteria bullet cover an R-OD request arriving after a U-U message with a higher number. See S1 for the scope of the highest-seen value. |
| B2 — AC-ENH-38 | Closed | Validation Criteria states both cases (failed write: discarded in Pass A, Helmet still equipped; successful write: runs in Pass A of the tick whose `Drain` delivered the outcome). Migration Plan step 3 carries the GDD correction. The GDD itself is unchanged, by the user decision of 2026-10-09. |
| C1 — connection-level route | Closed | One intake separates the two kinds from one sealed type table; connection-level messages go to an `IConnectionMessageSink`, which only records. See N1 for what the intake does not report. |
| C2 — classification | Closed | Decision 1 has 26 rows. Respec Phase 2 and `EnhancementAttemptRequest` are stated as not held and rejected with a closed gate; `AllocateFreePointRequest` is stated as rate-limited and therefore not held. |
| C3 — AC-MCR-03 in a release build | Closed | Decision 3 matches `MessageRoutingRegistry.ValidateAndRoute(…, isDevelopmentBuild)` and `PendingSchemaDispatchException`: throw at startup in a development build; skip, log and drop at intake in a release build. See S3 for how the switch reaches registration. |
| R1 — largest body | Closed | The sentence is gone; the table lists `SkillCastRequest` at 16. |
| R2 — ADR-002 dependency | Closed | In Depends On; ADR-002 is Accepted (2026-06-14). |
| R3 — Pass A isolation, hold entries of a removed connection | Closed | The Isolation rule covers a released request; Decisions 4 and 5 return the entries to the pool at the start of the next `DispatchTick`. |
| R4 — tick source | Closed | `IServerTickSource`; `ServerTickLoop.ServerTickNumber` is a public `uint` property. |
| R5, R6 | Open by design | After acceptance. |

### Amendment's own findings — confirmed
- `PartyChatRequest`: `ushort textByteCount` + up to 384 bytes = 386-byte body, 400 bytes standalone (`party-chat.md` lines 44–50). `MAX_INBOUND_MESSAGE_BYTES` = 400 fits it exactly.
- `HeartbeatMessage`: 10-byte envelope only (CR-NET-7.10). `ZoneSnapshotRequest` (22 bytes standalone), `ClientBackgrounded` and `RttProbeEcho` (14) carry `SenderEntityID`, as the table note says.

---

## New — fix before acceptance

### N1 — Inbound activity has no route to the session layer
- `networking-wire-protocol.md` CR-NET-7.10: "Any packet received from the client — including this one — resets the heartbeat timeout counter", and the client skips its heartbeat for an interval in which it sent anything else. The timeout fires after `HEARTBEAT_TIMEOUT_SECONDS` with "no packet of any type".
- `ConnectionStateMachine.RecordInboundActivity(accountId, currentTick)` exists for this. Nothing calls it.
- ADR-014 Decision 2: the adapter checks the length, calls `TryAccept` "and does nothing else". The intake calls a sink for connection-level types only; a request is copied to the inbox and nothing else learns that it arrived.
- As written, a player who is moving (20 Hz `MovementIntentMessage`) sends no heartbeat, the session layer sees none, and the session goes to `Disconnected_SessionActive` after the timeout.
- Fix: the intake reports activity for a connection — one interface member next to `IConnectionMessageSink`, called once per message, or a last-received tick per connection that the session layer reads. State at which step it is reported: a message from a known connection that is stale, malformed or dropped by a full inbox is still a packet from a live client. Add one test to Migration Plan step 1 (a request reports activity).

---

## New — small items

- **S1 — Scope of the U-U highest-seen value.** Risks, second bullet: "the stale check keeps each U-U type's own stream moving forward". Decision 2 keeps one value per connection across all U-U types, so a `SkillCastRequest` overtaken by the `MovementIntentMessage` sent 50 ms after it is dropped with no reply. **User decision 2026-10-09 (this review): the highest-seen value is kept per U-U type per connection.** Decision 2 (the intake's state, step 4, the advance rule) and the storage note change with it; the Risks bullet then holds as written. Four U-U types today.
- **S2 — Routing rows.** `MessageRoutingRegistry` has two client→server rows (`HeartbeatMessage`, `SetTarget`). Decision 3 reads the channel from the row and throws in a development build without one, and Migration Plan step 1 calls the table of Decision 1 "its registration list". Say that the dispatcher story registers the types that have a row and a message type in code, and that the story which adds a type adds its routing row with its registration.
- **S3 — Key Interfaces.** No member passes the development-build switch to registration (`Register` and `Seal` take none). How the intake reaches the type table and the connection set that `IInboundRequestDispatcher` owns is not stated (one object behind both interfaces, or a shared one).
- **S4 — Connection slots.** A removed connection keeps its arena segment and records until the next `DispatchTick`. `AddConnection` for a new client in the same tick interval, with all 50 slots taken, has no stated result.

---

## Cross-ADR Conflicts

None new. The table of the prior review stands. N1 is a gap between ADR-014 and the session layer's existing code and CR-NET-7.10, not a conflict with another ADR.

## Engine Compatibility

Unchanged: 14 / 14 ADRs with the section; no deprecated API; Verification Required 1–6 stay with the adapter story.

## GDD Revision Flags

Unchanged from the prior review (AC-ENH-38 wording, the `networking-core.md` Constraint 3 note), both after acceptance. Seen by the amendment and still open: `RttProbeEcho` has no body in `networking-wire-protocol.md` and a `probeSequence` field in `networking-channel-contract.md` line 124.

## Verdict

**ADR-014: CONCERNS — not ready for acceptance, by one item.** The amendment closed everything it set out to close and the two things it found on its own hold. N1 is small to fix and has to be in the ADR, because the ADR says the adapter and the intake do nothing beyond what it lists.

Next, in one authoring session: amend ADR-014 for N1 and S1–S4. A further review is not needed if the amendment stays within those five items; then the user marks it Accepted. After acceptance, as before: the AC-ENH-38 wording, R5, R6, the registry entries, `/create-control-manifest update`, the dispatcher story.

**Project-wide: CONCERNS**, unchanged.
