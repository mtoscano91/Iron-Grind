# Epic: Networking Core

> **Layer**: Foundation
> **GDD**: design/gdd/networking-core.md + 9 sub-contracts
> **Architecture Module**: Networking Core
> **Status**: Stories Complete (45/45 — the headless-build engine-risk gate below is still open: A1-1, A1-2, A1-7 and the final A1-4 need the Linux IL2CPP build; Stories 042–045, the server composition root of ADR-009 Amendment 1, added 2026-10-22 — Story 045 Complete 2026-10-22 (EditMode 3533 total, 3463 passed, 0 failed, 70 skipped; PlayMode 27/27; Windows Dedicated Server runs in `production/qa/evidence/networking-core-045-zone-process-verification.md`); Story 044 Complete 2026-10-22 (EditMode 3523 total, 3453 passed, 0 failed, 70 skipped; Database 5/5; Story 041 listener wake-on-stop fix, ADR-015 note 2026-10-22); Story 043 Complete 2026-10-22 (EditMode 3484 total, 3419 passed, 0 failed, 65 skipped); Story 042 Complete 2026-10-22 (EditMode 3374 total, 3309 passed, 0 failed, 65 skipped); Story 041 Complete 2026-10-19 (EditMode 3146 / 3147, 1 explicit skip; two-process verification run in `production/qa/evidence/networking-core-041-relay-verification.md`); Story 040 Complete 2026-10-16 (EditMode 3053 / 3053); Story 041 added 2026-10-16 for ADR-015, Ready 2026-10-19 (`/story-readiness`; Character Persistence Story 012 Complete); the headless-build engine-risk gate of ADR-004 is still open; Story 039, the production `IClientMessageOutbox`, Complete 2026-10-12 — EditMode 2487 / 2487 and a PlayMode loopback run 20 / 20 in the Editor, VS-1 to VS-5 PASS in `production/qa/evidence/networking-core-039-ngo-send-verification.md`; Story 038, the inbound transport adapter, Complete 2026-10-12 — EditMode 2420 / 2420 and a PlayMode loopback run 12 / 12 in Unity batch mode, ADR-014 Verification Required 1–6 all PASS in `production/qa/evidence/networking-core-038-ngo-verification.md`; both written 2026-10-09 after ADR-004 OQ-ADR4-3 was resolved (Amendment 1: unnamed custom messages); Story 037, the tick pipeline of ADR-014 Migration Plan step 2, added and Complete 2026-10-09, full EditMode suite 2203 / 2203 — Stories 035 and 036 added 2026-10-09 for ADR-014 Migration Plan step 1; 035 Complete 2026-10-09, full EditMode suite 2083 / 2083; 036 Complete 2026-10-09, full EditMode suite 2182 / 2182). Stories 032, 033 and 034, the three ADR-012 assembly-move stories, Complete 2026-10-08. Stories 001–031 Complete — Story 031 (added 2026-10-07, ADR-011 Migration step 2) Complete 2026-10-07; full EditMode suite 1900 / 1900 in Unity batch mode. Story 030 (added 2026-10-07 after ADR-011) Complete 2026-10-07. Stories 001–029 Complete (2026-07-22). Not yet confirmed via a full live Unity Editor test run across the whole epic together; see `production/session-state/active.md` for verification status per story.
> **Stories**: 45 stories created (001–045)

## Overview

Networking Core is the multiplayer substrate for Project Iron Grind — the module that all other networked systems plug into. It owns the 10-byte project envelope, channel routing, 20Hz server tick loop, and batch framing via NGO's `CustomMessagingManager`. This epic spans the root GDD plus nine sub-contract GDDs that collectively define every facet of the network layer: session lifecycle (ST-NET-1/2 state machines), wire protocol (message schemas, serialization), session token (NSCRT), ghost entity/character state, OWL latency compensation, message criticality classification, channel contract, relevance filter, and test harness. The wire contract is project-owned; NGO is the transport substrate only. A library swap must never touch the wire format (ADR-004 Decision 4). All HIGH-risk Unity 6.3 NGO API changes are verified in ADR-004 before any implementation sprint is greenlit.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-004: Networking — NGO Substrate | 10-byte envelope, 20Hz tick via `NetworkManager.ServerTime.Tick`, `CustomMessagingManager` for all project messages, three delivery channels (U-U/R-OD/R-U), batch ≤512B/client/tick | HIGH — `NetworkTransform.OnUpdate` (renamed from `Update` in 6.3); `CustomMessagingManager` API post-cutoff |
| ADR-001: Purchase Integrity | R-OD dedup key = `(charId, messageType, requestId)`, TTL = 300s; `messageType` resolved from envelope field | LOW |
| ADR-009: Scene/Zone-Load Management | Tick loop must not start until `ZoneNavigationService.Initialize()` completes; gateway registration only after tick is active | HIGH (Unity 6.3 `Scene.handle` type change) |
| ADR-010: Event/Messaging Architecture | NGO RPCs and custom messages are the network boundary; they are NOT "events" in the ADR-010 sense — ADR-010 governs intra-process messaging only | LOW |
| ADR-011: Asynchronous Persistence in the Server Tick Loop | Tick-driven code never awaits a persistence `Task`; `ITickCompletionQueue.Drain` handles results on the tick thread; `ICharacterMutationGate` records an irreversible write in flight | MEDIUM — where an `await` continuation resumes; headless-build checks gate shipping |
| ADR-014: Inbound Request Dispatch and Tick Order | One `InboundRequestDispatcher` per zone is the only path from a client message to a game system: intake into a bounded inbox outside the tick; on the tick, release of held requests, then guard chain → character → mutation gate → handler; `ZoneTickPipeline` fixes the tick order | LOW for the dispatcher and the pipeline; HIGH for the transport adapter (NGO receive API, Verification Required 1–6) |
| ADR-015: Cross-Zone Message Relay | `IServerWideBroadcaster` delivers to this zone's outbox first, then publishes one `pg_notify` on `irongrind_relay`; each zone's background listener feeds a bounded inbox drained by tick step 1a; allowlist (`ServerBroadcast_Enhancement9` only), at most once | LOW for the game side (Story 040); MEDIUM for the Npgsql transport (IL2CPP background thread, Npgsql version not pinned — Story 041) |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-net-001 | 10-byte project envelope: `messageType:ushort` (2B), `charId:uint` (4B), `sequenceNum:uint` (4B); all messages use this envelope | ADR-004 ✅ |
| TR-net-002 | Server tick at exactly 20Hz via `NetworkManager.ServerTime.Tick`; `Application.targetFrameRate = 20` on server (bounds NavMeshAgent sim, ADR-002) | ADR-004 ✅ |
| TR-net-003 | Per-client batch ≤512B per tick; relevance-filtered before send; batch built server-side (networking-relevance-filter sub-contract) | ADR-004 ✅ |
| TR-net-004 | R-OD dedup key = `(charId, messageType, requestId)`; TTL = SESSION_TTL_SECONDS (300s); `messageType` resolved from envelope field, never hardcoded numerically | ADR-001 ✅ |
| TR-net-005 | Channel routing: U-U for movement, R-OD for combat/economy (with dedup), R-U for critical state — per criticality classification (networking-message-criticality sub-contract) | ADR-004 ✅ |
| TR-net-006 | Session state machine ST-NET-1/2; ghost entity policy on disconnect; NSCRT token issues/validates session identity (networking-session, networking-session-token, networking-ghost-session sub-contracts) | ADR-004 ✅ |
| TR-net-007 | OWL latency compensation algorithm with `LastBeatServerTick` per entity slot; tick-window constraint proven correct (networking-owl-compensation sub-contract) | ADR-004 ✅ |
| TR-net-008 | Tick loop start gated on `ZoneNavigationService.Initialize()` completion; tick loop must be active before gateway registration | ADR-009 ✅ |
| TR-net-009 | `ITransportFaultInjector`, `IServerCrashInjector`, `INetworkTestObserver` test harness present; harness code stripped from release builds (networking-test-harness sub-contract) | ADR-004 ✅ |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Each of the 10 sub-contract GDDs has its own full acceptance criteria list; populate the registry (one TR-ID per AC) before running `/story-readiness` checks.

> **Engine risk gate**: ADR-004 Validation Criteria require empirical verification of `CustomMessagingManager` and `NetworkManager.ServerTime.Tick` behavior in a Unity 6.3 headless build before the Networking Core implementation sprint is greenlit. Run this check before the sprint opens. Story 045 (2026-10-22) verified A1-3, A1-5, A1-6 and a first A1-4 on a Windows Mono Dedicated Server build (`production/qa/evidence/networking-core-045-zone-process-verification.md`). A1-1, A1-2, A1-7 and the final A1-4 are NOT RUN and stay with this gate.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria across all 10 sub-contract GDDs are verified
- Logic stories (envelope serialization, dedup, OWL algorithm) have passing test files in `tests/EditMode/Networking/`
- Integration stories (session lifecycle, ghost entity, fault injection) have tests in `tests/PlayMode/Networking/` or documented playtest evidence in `production/qa/evidence/`
- ADR-004 engine-risk verification gate passed (headless build check) and evidence filed before sprint opens

## Sub-Contract GDDs

| Sub-contract | File | Status |
|---|---|---|
| Networking Session Lifecycle | design/gdd/networking-session.md | Approved |
| Networking Wire Protocol | design/gdd/networking-wire-protocol.md | Approved |
| Networking Test Harness | design/gdd/networking-test-harness.md | Approved |
| Networking Session Token | design/gdd/networking-session-token.md | Approved |
| Networking Ghost Session | design/gdd/networking-ghost-session.md | Approved |
| Networking Ghost Character State | design/gdd/networking-ghost-character-state.md | Approved |
| Networking Message Criticality | design/gdd/networking-message-criticality.md | Approved |
| Networking Channel Contract | design/gdd/networking-channel-contract.md | Approved |
| Networking Relevance Filter | design/gdd/networking-relevance-filter.md | Approved |
| Networking OWL Compensation | design/gdd/networking-owl-compensation.md | Approved |

## Stories

*Work through in order — each story's `Depends on:` field tells you what must be Done first. Test Harness (001-002) unlocks nearly everything else; Wire Protocol Core (003-008) is the next foundational layer.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Test Harness: Fault/Crash/Zone-Config Injection | Logic | Complete | ADR-004 |
| 002 | Test Harness: INetworkTestObserver + Release-Build Stripping | Logic | Complete | ADR-004 |
| 003 | Message Envelope & Fixed-Point Primitive Serialization | Logic | Complete | ADR-004 |
| 004 | EntityID/Enum Wire-Safety Guards | Logic | Complete | ADR-004 |
| 005 | Version/SequenceNumber Stale-Discard Helpers | Logic | Complete | ADR-004 |
| 006 | Priority-Path Cap & Two-Path Delivery Model | Logic | Complete | ADR-004 |
| 007 | R-U/U-U Batch Framing, Buffer Pooling & Overflow Policy | Integration | Complete | ADR-004 |
| 008 | Heartbeat Message & IL2CPP AOT Guardrails | Logic | Complete | ADR-004 |
| 009 | Fixed 20Hz Server Tick Loop | Logic | Complete | ADR-004 |
| 010 | Cross-Cutting RPC Guards | Logic | Complete | ADR-004 |
| 011 | Commit-Before-Broadcast Generic Pattern | Logic | Complete | ADR-001 |
| 012 | Player Connection State Machine — Core Transitions | Logic | Complete | ADR-004 |
| 013 | Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth | Integration | Complete | ADR-001 + ADR-004 |
| 014 | Zone Session State Machine & Capacity Enforcement | Logic | Complete | ADR-004 |
| 015 | TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases | Integration | Complete | ADR-004 |
| 016 | Session Token Generation, Validation & Rotation (NSCRT) | Logic | Complete | None (pure crypto, no ADR applies) |
| 017 | Ghost Promotion & State Constraints | Logic | Complete | ADR-004 |
| 018 | Pre-Disconnect Snapshot & Write-Ordering | Logic | Complete | ADR-004 |
| 019 | Ghost Death & Mob De-Targeting | Logic | Complete | ADR-004 |
| 020 | Ghost Reward Forfeit Policy — Two-Pool XP & Party Slot Retention | Logic | Complete | ADR-004 |
| 021 | Ghost Cleanup, Zone Crash & Voluntary Dismissal | Integration | Complete | ADR-004 |
| 022 | LastBeatServerTick Slot Allocation & Data Structure | Logic | Complete | ADR-004 |
| 023 | OWL Wrap-Correction Compensation Formula | Logic | Complete | ADR-004 |
| 024 | OWL Threshold Suspension & Hysteresis Signal | Logic | Complete | ADR-004 |
| 025 | Message Criticality/Channel Routing Table & Unclassified-Message Fallback | Logic | Complete | ADR-004 |
| 026 | GoldSyncEvent Forced-Delivery Overflow Policy | Logic | Complete | ADR-004 |
| 027 | SelfDamageEvent vs DamageEvent Delivery Exclusivity | Logic | Complete | ADR-004 |
| 028 | EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm | Logic | Complete | ADR-004 |
| 029 | SetTarget RPC & Target Slot Management | Logic | Complete | ADR-004 |
| 030 | Tick Completion Queue and Character Mutation Gate | Logic | Complete | ADR-011 |
| 031 | Irreversible-Outcome Coordinator and Shared Failure Protocol | Logic | Complete | ADR-011 |
| 032 | [Move Networking Server State to the Server Assembly (1 of 3)](story-032-server-assembly-move-state-and-tick.md) | Integration | Complete (2026-10-08) | ADR-012 |
| 033 | [Move the WireProtocol Server Half to the Server Assembly (2 of 3)](story-033-server-assembly-move-wire-protocol-server-half.md) | Integration | Complete (2026-10-08) | ADR-012 |
| 034 | [Move the Networking Test Harness; Put the Shared Types on the Allow-List (3 of 3)](story-034-server-assembly-move-test-harness-and-shared-list.md) | Integration | Complete (2026-10-08) | ADR-012 |
| 035 | [Guard Chain Changes for the Request Dispatcher](story-035-guard-chain-changes-for-dispatch.md) | Logic | Complete | ADR-014 |
| 036 | [Inbound Request Dispatcher — Intake, Dispatch and Hold Queues](story-036-inbound-request-dispatcher.md) | Logic | Complete | ADR-014 |
| 037 | [Zone Tick Pipeline — the Order of One Server Tick](story-037-zone-tick-pipeline.md) | Logic | Complete | ADR-014 |
| 038 | [Transport Adapter — Inbound Messages and Connections](story-038-transport-adapter-inbound.md) | Integration | Complete (2026-10-12; PlayMode loopback run, VR-1 to VR-6 PASS) | ADR-014, ADR-004, ADR-012 |
| 039 | [Client Message Outbox — the Production Send Path for Priority Messages](story-039-client-message-outbox.md) | Integration | Complete (2026-10-12; EditMode 2487 / 2487, PlayMode 20 / 20, VS-1 to VS-5 PASS) | ADR-004, ADR-014, ADR-012 |
| 040 | [Cross-Zone Relay — the Game Side, on an In-Memory Bus](story-040-cross-zone-relay-game-side.md) | Integration | Complete (2026-10-16; EditMode 3053 / 3053) | ADR-015, ADR-014, ADR-012 |
| 041 | [Cross-Zone Relay — the PostgreSQL Transport (Publisher, Listener, Stop)](story-041-cross-zone-relay-npgsql-transport.md) | Integration | Complete (2026-10-19, COMPLETE WITH NOTES; Windows Dedicated Server Mono run: VR-0, 1, 3 PASS, VR-5 part 1 PASS, part 2 FAIL accepted as TD-074, VR-2 carried, VR-4 and VR-6 moved; listener on WaitAsync) | ADR-015, ADR-006, ADR-011 |
| 042 | [Zone Server Composition — Lifecycle Shell, Teardown 1–8, Exit Codes, Quit Interception](story-042-zone-server-composition-lifecycle-teardown.md) | Logic | Complete | ADR-009 A1, ADR-011, ADR-015 |
| 043 | [Zone Startup S0–S10, S-fail and ZoneServerConfig](story-043-zone-startup-sequence-and-config.md) | Logic | Complete | ADR-009 A1, ADR-006 A3 |
| 044 | [Composition Wiring — Connection Pool and Warm-up, the Object Graph, Listener, Dispose](story-044-zone-composition-wiring-database-graph-listener.md) | Integration | Complete | ADR-009 A1, ADR-006 A3, ADR-013, ADR-014, ADR-015 |
| 045 | [ZoneServerHost, the Unity Seams, the Server Scene and Local Verification](story-045-zone-server-host-unity-seams-verification.md) | Integration | Complete | ADR-009 A1, ADR-004, ADR-012, ADR-015 |

*Stories 040 and 041 are ADR-015 Migration Plan steps 1 and 2 (written 2026-10-16). Enhancement Story 016 waits for 040 only. By user decision 2026-10-16, the two call sites of `NpgsqlRelayListener.Stop()` (ADR-009 teardown step 2a, server host `OnDestroy`) and ADR-015 Verification 4 and 6 belong to the future server composition-root / ADR-009 teardown story, not to 041.*

*Stories 042–045 are ADR-009 Amendment 1 Migration Plan step 2 (written 2026-10-22): the composition class and teardown (042), startup and config (043), the real object graph and database (044), the Unity host and the local runs (045). User decisions 2026-10-22: server config is a JSON file named by `-serverConfig`; S2 static data goes through `IZoneStaticDataLoader` with a stand-in, and S5 builds only the networking and persistence objects.*

*ADR-014 Migration Plan step 4 (the adapter story) is Story 038; ADR-004 OQ-ADR4-3 was resolved on 2026-10-09 (ADR-004 Amendment 1). Story 039 is the outbound half, split out by user decision 2026-10-09. `com.unity.netcode.gameobjects` 2.13.3 was added to `Packages/manifest.json` the same day; the engine facts both stories rely on are in `docs/engine-reference/unity/modules/networking.md` and were read from source on 2026-10-09; Story 038 confirmed part of them in a loopback run on 2026-10-12 (not in a headless build).*

**Scoped out of this epic** (owned by other systems' future epics, using these GDDs as their wire-contract reference): every specific downstream message schema for Auto-Attack Combat, Currency, Leveling, Zone Instancing, Party, Inventory, Equipment, NPC Shop, Consumable Use, Movement, and Skill systems. Networking Core owns the envelope/channel/tick/session/ghost/OWL/relevance-filter/test-harness substrate only.

**Known blockers/inconsistencies to resolve before final sign-off** (do not block starting implementation, but should be tracked):
- OQ-NET-1 (BLOCKING, design): `HEARTBEAT_TIMEOUT_SECONDS` production default undetermined (recommended 8-12s) — stories 012/013/017 use test-injected override values and are unaffected, but the production default must be set before launch.
- Cross-doc AC-ID collision: root `networking-core.md` and `networking-wire-protocol.md` each independently define an unrelated "AC-NC-31" (Story 024 vs. Story 004 respectively).
- Cross-doc AC-ID collision: `networking-wire-protocol.md` (line 1696, "skip-on-activity heartbeat scheduling," Story 008) and `networking-session.md` (line 359, "REAUTH_FAILURE_LIMIT exhausted") each independently define an unrelated "AC-NC-38". Found by unity-specialist code review during Story 008; Story 008's implementation correctly uses the wire-protocol meaning.
- Cross-doc constant inconsistency: `GHOST_COMBAT_TTL_MINUTES` (`networking-session.md`, flat 60s) vs. `GHOST_COMBAT_TTL_MIN_S` (`networking-ghost-session.md` F-GH-1, formula-based 30s baseline) — Stories 019/021 use the ghost-session formula as authoritative pending a design decision.
- `StatID` enum cross-doc dependency: Character Stats GDD's `StatID` needs `enum:uint`→`enum:byte` before Story 004 can drop its byte-transmission workaround.
- ADR-004's engine-risk profiling gate (verify `CustomMessagingManager` + `NetworkManager.ServerTime.Tick` in a real Unity 6.3 headless build) must pass before the implementation sprint is greenlit, per this epic's own "Engine risk gate" note above.
- ~~Server composition root / ADR-009 teardown story (not written)~~ — **written 2026-10-22 as Stories 042–045** (warm-up: 044 AC-4; listener `Stop()` from step 2a and `OnDestroy`: 042 AC-3 / AC-11, 045 AC-11; ADR-015 V4 and V6: 045 AC-10 / AC-11). Original note: must call `NpgsqlRelayListener.Stop()` from teardown step 2a and from the server host `OnDestroy`, and carry ADR-015 Verification 4 and 6 (user decision 2026-10-16; open item C2). It must also warm the publisher's `NpgsqlDataSource` before the tick loop starts (one `CreateCommand` + `ExecuteNonQueryAsync` waited on at startup), because the first publish of a fresh process cost 58 ms on the tick in Story 041 VR-1 (user decision 2026-10-19). On a Mono build or in the Editor, the listener connection must keep `WaitAsync` (ADR-015 note 2026-10-19).
- Composition-root stand-ins (Stories 042–045, user decision 2026-10-22): each logs `"[ZoneServer] <step> not implemented"` once and does nothing until its owner ships — S2 static data loaders (no production loader exists for `ItemDatabase`, `ClassRegistry`, `LootTableRegistry`, `DamageCalculationConfig`, `EnhancementConfig`); game systems and their handler registrations in S5 (e.g. `EnhancementInboundRegistration`); `IZoneNavigation`, `IZoneBoundsValidator`, the zone `State` owner, `IZoneCloseSteps` (CR-ZI-12, including the step 5 ordering note of ADR-009 A1), `IGatewayRegistration` (Zone Instancing / Navigation); `IConnectionCharacterDirectory` and `IConnectionActivitySink` (no production implementation — until then every request is dropped as `NoCharacter`). A1-1, A1-2, A1-7 and the final A1-4 join the Linux IL2CPP headless-build gate.
- TR-ID collision: Story 009 (Fixed 20Hz Server Tick Loop) and Story 011 (Commit-Before-Broadcast Generic Pattern) both cite `Requirement: TR-net-002` in their headers — but this epic's own placeholder table above (row `TR-net-002`) defines that ID as "Server tick at exactly 20Hz," which is Story 009's territory, not Story 011's CR-NET-5 pattern. Found during Story 011's `/story-done` closure. Did not block Story 011 (implementation correctly sourced its requirement directly from `networking-core.md` CR-NET-5, not from the empty `tr-registry.yaml`), but resolve when the registry is actually populated — Story 011 needs its own TR-ID.

## Next Step

Run `/story-readiness [story-path]` on Story 001 to confirm implementation-readiness, then `/dev-story` to begin.
