# Architecture Review Report — ADR-014

> **Date:** 2026-10-09
> **Engine:** Unity 6.3 LTS (6000.3)
> **ADR reviewed:** ADR-014 Inbound Request Dispatch and Tick Order (Proposed, `4d0bfb2`)
> **Mode:** `/architecture-review` — run as a review of ADR-014 only
> **Prior reviews:** `architecture-review-2026-10-07.md` (full, CONCERNS, 11 ADRs; P1 is the finding ADR-014 answers); `architecture-review-2026-10-08.md`, `architecture-review-2026-10-08-rereview.md` (ADR-012); `architecture-review-2026-10-08-adr-013.md`
> **Verdict:** **CONCERNS — ADR-014 stays Proposed.** Two items to fix before acceptance (B1, B2), three concerns for the same authoring pass (C1–C3), six small items (R1–R6). The project-wide verdict stays **CONCERNS**.

## Scope Note

- **Read in full:** ADR-014, ADR-011, `architecture-review-2026-10-08-adr-013.md`; `CrossCuttingRpcGuardChain.cs`, `RpcTypeTag.cs`, `ServerTickLoop.cs`, `CharacterMutationGate.cs`, `IrreversibleOutcomeCoordinator.cs`.
- **Read in part:** ADR-002, ADR-004 and ADR-010 and the control manifest at the points ADR-014 relies on; `ClientEntityMessageEnvelope.cs`, `StaleDiscardComparer.cs`, `MessageRoutingRegistry.cs`; the GDDs at the lines ADR-014 cites (`enhancement-system.md`, `networking-core.md`, `networking-wire-protocol.md`, `networking-channel-contract.md`, `networking-message-criticality.md`) and, by search, every client→server message in the wire protocol; Enhancement Story 009.
- **Not run:** the `unity-specialist` consultation (it validated this ADR on the day it was written, and `docs/engine-reference/unity/` has no entry on `CustomMessagingManager`, `FastBufferReader` or named/unnamed messages to check anything against); the full traceability matrix (Phases 2–3). `tr-registry.yaml` left as it is. `docs/consistency-failures.md` does not exist.

---

## Claims Checked

### Against the code — confirmed
- `GetRequiredTickGap` throws `ArgumentOutOfRangeException` for a tag it does not list; `SetTarget` returns 0 (Decision 4a.1).
- `Evaluate` logs every rejection with an interpolated string (Decision 4a.3) and writes `_lastAcceptedTick` only on `Accepted`, so a read-only `IsLiveOwner` is needed for Pass A (Decision 4a.2).
- The guard chain holds the `clientId ↔ EntityID` registry and the session-ready set; nothing maps a connection to a `CharacterID` (Risks, fourth bullet).
- `ServerTickLoop.AdvanceTick` increments `ServerTickNumber` before the delegates run, and an exception from a tick-driven delegate skips the TTL pass, the drift sample and the observer (Decision 6).
- `CharacterMutationGate.Open` invokes a multicast event with no per-subscriber isolation (Alternative 6).
- Nothing in `src/` calls `RegisterTickDriven`.
- `ClientEntityMessageEnvelope.WireSize` is 14 and carries `SenderEntityId`.
- `IrreversibleOutcomeCoordinator.Begin` returns `RejectedWriteInFlight` when the gate is already held, before any delegate runs.

### Against the ADRs and GDDs — confirmed
- ADR-011 Decision 4: the held request types, release after `Drain` and before new requests, the bound of 16, discard after a failed write.
- ADR-002: phases 1–4 as Decision 6 names them.
- ADR-010: the dated note on Decision 5 is present.
- `networking-core.md` Cross-Cutting Constraints 1–3 (lines 283–287) and `MAX_PLAYERS_PER_ZONE` = 50 (line 417).
- `enhancement-system.md` CR-ENH-18 (line 135) is quoted accurately.
- Loot bids move no gold at bid time (`loot-table-system.md` CR-LT-9.1: gold is spent at auction teardown, a server-originated step), so `HeldDuringIrreversibleWrite` = false for the bid request is consistent with Decision 3's own criterion.

---

## Blocking — fix before acceptance

### B1 — The stale-sequence check at intake can drop reliable requests
- Decision 2: "Every request type is sent on the reliable ordered channel (R-OD in `networking-wire-protocol.md`)".
- `networking-wire-protocol.md` line 1162: `MovementIntentMessage` is U-U, sent at 20 Hz. Line 1196: `SkillCastRequest` is U-U. Decision 3 names movement, and Decision 1 names skill use, as requests. Heartbeat (line 291) and `RttProbeEcho` are U-U too and, by Decision 1, share the same check.
- `networking-channel-contract.md` CCR-1: `SequenceNumber` is one counter per connection, shared across all message types.
- Decision 2 step 2 drops a message whose `SequenceNumber` is stale for the connection, against one highest-seen value.
- An unreliable packet sent after a reliable one can arrive first — always when the reliable one is retransmitted. Its higher number becomes the highest seen; the reliable `BuyRequest` or `EnhancementAttemptRequest` then arrives with a lower number and is dropped, with no reply.
- A player who is moving while tapping a button on a lossy mobile link is the ordinary case, not an edge case.
- Options: apply the stale check to U-U types only (the transport already orders and de-duplicates R-OD); or keep one highest-seen value per channel.
- Also restrict "for one connection receive order is the client's send order" to R-OD types, and Verification Required 5 with it.

### B2 — AC-ENH-38 cannot pass as Validation Criteria state it
- `enhancement-system.md` lines 698–701: the write returns `DatabaseError`; "The held unequip request is processed only after that and fails for lack of bag space."
- The same GDD, lines 120 and 146: a failed commit disconnects the client (CR-CP-5).
- ADR-011 Decision 4: "If the gate opened because of a failed write, the client is being disconnected and the held requests are discarded." ADR-014 Pass A implements that through `IsLiveOwner`.
- ADR-014 Validation Criteria, second bullet, repeats the AC: the unequip "runs only after the rollback, and fails for lack of bag space".
- In production the unequip is discarded and never runs. A test would pass only with a disconnect fake that does not remove the connection.
- Fix: revise AC-ENH-38 — after the failed write the held request is discarded and the Helmet is still equipped — and add a success-path case in which the held request runs after the outcome is delivered. Reword the Validation Criteria bullet. Add the AC correction to Migration Plan step 3, next to the "pickup" correction for Story 009.

---

## Concerns — same authoring pass

### C1 — Connection-level messages have no route
- Decision 1: handshake, heartbeat, `ClientBackgrounded` / `ClientForegrounded` "share the adapter and the stale-sequence check of Decision 2", have no descriptor and are not requests.
- Decision 2: the adapter checks the length, decodes the envelope, calls `TryAccept`, "and does nothing else". `TryAccept` step 3 drops any type with no registered descriptor as `UnknownInboundMessageType`.
- As written, every heartbeat is dropped and logged as an anomaly.
- Not stated: which component separates the two kinds of message, and which owns the per-connection highest-seen `SequenceNumber` that both read.

### C2 — No complete classification of client→server types
- `networking-wire-protocol.md` defines about 25 client→server messages. Decision 1 and Decision 3 classify by example.
- Not placed: respec Phase 1 / Phase 2, `AllocateFreePoint`, `OpenNPCInteraction` / `CloseNPCInteraction`, `GhostDismissRequest`, `ZoneSnapshotRequest`, `RttProbeEcho`.
- Respec Phase 2 consumes a scroll from the bag and is itself an irreversible outcome. It is not in the held list; with the gate closed the coordinator answers `RejectedWriteInFlight` (carried item P2). The ADR should say that this is the intended result, or hold it.
- `AllocateFreePoint` is rate-limited, so by Decision 3 it cannot be a held type; it does not touch the bag, so that is consistent — worth one line.
- Suggested: one table — message type, request or connection-level, `HeldDuringIrreversibleWrite`, `RpcTypeTag`. It closes C1 and C2 and gives the dispatcher story its registration list.

### C3 — AC-MCR-03 in a release build
- `networking-message-criticality.md` line 241: a type with no routing row raises `PendingSchemaDispatch` at startup in debug builds; in release builds it is logged and "No crash occurs in either configuration".
- `MessageRoutingRegistry.ValidateAndRoute` implements that split with an `isDevelopmentBuild` parameter.
- Decision 3 throws at startup in both.
- State the release behaviour: for example, the registration is skipped and logged, and the type is then dropped at intake as `UnknownInboundMessageType`.

---

## Small items

- **R1** — Decision 2 step 3: "the largest client→server body in `networking-wire-protocol.md` today is 12 bytes". `SkillCastRequest` is 16 (line 1196). `MAX_INBOUND_BODY_BYTES` = 64 still covers it.
- **R2** — Decision 6 builds on ADR-002's phases; ADR-002 is under Related Decisions but not under Depends On.
- **R3** — The per-request `try`/`catch` is stated for Pass B steps 1–4, not for a handler called from Pass A. When the hold-queue entries of a removed connection are returned to the pool is not stated ("frees the storage of connections removed" reads as inbox storage).
- **R4** — `InboundRequestContext.ArrivalTick` is the tick number when `TryAccept` ran, so the inbox needs a tick source. None is in Key Interfaces.
- **R5** — Guards run at dequeue with one `currentTick` for the whole pass. Two `SkillCastRequest`s sent 50 ms apart that land in the same tick interval get the same tick; the second is rejected (`NotifySkillUsed` gap = 1 tick). The guard chain behaved this way before this ADR; with the evaluation point now fixed it is certain rather than incidental. A note for `networking-core.md` Cross-Cutting Constraint 3, not a change to this ADR.
- **R6** — `ADR-012` lines 26 and 304 and `ADR-013` line 284 still say ADR-014 is "not yet written".

---

## Cross-ADR Conflicts

None. ADR-014 against each ADR it touches:

| ADR | Relation | Result |
|---|---|---|
| ADR-002 | Simulation phases 1–4 inside step 3 of the pipeline | Consistent; R2 |
| ADR-004 | Envelope over the transport; clock source (Decision 5); OQ-ADR4-3 open | No conflict. How `AdvanceTick` is driven and its relation to NGO's tick is left open by the Ordering Note, as before |
| ADR-010 | Decision 5: callbacks enqueue | Rule kept, example replaced; dated note present |
| ADR-011 | Gate, hold rule, `Drain` before game logic | Consistent. B2 is a conflict between ADR-011 and the GDD's AC, repeated in ADR-014's Validation Criteria |
| ADR-012 | Dispatcher in `IronGrind.ServerLogic`; handlers registered by the composition root | Consistent; R6 |
| ADR-001 | Shop request dedup stays in the system | Consistent |

C1 and C2 of 2026-10-07 are unchanged.

## ADR Dependency Order

No cycle. ADR-014 depends on ADR-004, ADR-010, ADR-011 and ADR-012, all Accepted.

```
Level 3:  ADR-012  Server/Client Assembly Boundary → ADR-007, ADR-004, ADR-010
Level 4:  ADR-011  Async Persistence in the Tick   → ADR-006, ADR-007, ADR-010
Level 5:  ADR-013  Server Random Provider          → ADR-011, ADR-012
          ADR-014  Inbound Request Dispatch        → ADR-004, ADR-010, ADR-011, ADR-012   [Proposed]
```

Enhancement Story 009 stays Blocked while ADR-014 is Proposed.

## Engine Compatibility

Engine: Unity 6.3 LTS (6000.3). ADRs with an Engine Compatibility section: 14 / 14. No deprecated API referenced. No stale version reference. ADR-014 uses no post-cutoff API in its decision.

- Verification Required 1–6 are attached to the adapter story and stay open by design. Item 5 needs the R-OD restriction of B1.
- `docs/engine-reference/unity/` has no entry on NGO custom messaging; nothing there confirms or contradicts the adapter's assumptions.

## GDD Revision Flags

No GDD assumption conflicts with verified engine behaviour. Wording to correct, with no status change (user decision 2026-10-09):

| GDD | Line | Correction |
|---|---|---|
| `enhancement-system.md` | AC-ENH-38 (698–701) | After the failed write the held request is discarded, not processed; add a success-path case (B2) |
| `networking-core.md` | Cross-Cutting Constraint 3 (287) | Note on two requests arriving within one tick interval (R5) |

## Architecture Document Coverage

Unchanged: `architecture.md` is still at 8 ADRs.

---

## Project-Wide State

Carried from 2026-10-07, unchanged: C1, C2, P2–P5, G1, G2, the `architecture.md` refresh, the empty TR registry. P1 is answered by ADR-014 and closes when it is Accepted.

## Verdict

**ADR-014: CONCERNS — not ready for acceptance.** The dispatcher design — one inbox, two passes, guard before gate, a fixed pipeline — is sound and matches ADR-011. The intake rules of Decision 2 are not: B1 loses reliable requests, and C1 leaves connection-level messages without a path.

Next, in one authoring session: amend ADR-014 for B1, B2 and C1–C3 (a classification table of the client→server types covers C1 and C2), fold in R1–R4. Then a lean re-review against this report, then acceptance. After acceptance: the AC-ENH-38 wording, the registry entries, `/create-control-manifest update`, the dispatcher story.

**Project-wide: CONCERNS**, unchanged.
