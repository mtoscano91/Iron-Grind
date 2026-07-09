# Epic: Networking Core

> **Layer**: Foundation
> **GDD**: design/gdd/networking-core.md + 9 sub-contracts
> **Architecture Module**: Networking Core
> **Status**: Ready
> **Stories**: 29 stories created (001–029)

## Overview

Networking Core is the multiplayer substrate for Project Iron Grind — the module that all other networked systems plug into. It owns the 10-byte project envelope, channel routing, 20Hz server tick loop, and batch framing via NGO's `CustomMessagingManager`. This epic spans the root GDD plus nine sub-contract GDDs that collectively define every facet of the network layer: session lifecycle (ST-NET-1/2 state machines), wire protocol (message schemas, serialization), session token (NSCRT), ghost entity/character state, OWL latency compensation, message criticality classification, channel contract, relevance filter, and test harness. The wire contract is project-owned; NGO is the transport substrate only. A library swap must never touch the wire format (ADR-004 Decision 4). All HIGH-risk Unity 6.3 NGO API changes are verified in ADR-004 before any implementation sprint is greenlit.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-004: Networking — NGO Substrate | 10-byte envelope, 20Hz tick via `NetworkManager.ServerTime.Tick`, `CustomMessagingManager` for all project messages, three delivery channels (U-U/R-OD/R-U), batch ≤512B/client/tick | HIGH — `NetworkTransform.OnUpdate` (renamed from `Update` in 6.3); `CustomMessagingManager` API post-cutoff |
| ADR-001: Purchase Integrity | R-OD dedup key = `(charId, messageType, requestId)`, TTL = 300s; `messageType` resolved from envelope field | LOW |
| ADR-009: Scene/Zone-Load Management | Tick loop must not start until `ZoneNavigationService.Initialize()` completes; gateway registration only after tick is active | HIGH (Unity 6.3 `Scene.handle` type change) |
| ADR-010: Event/Messaging Architecture | NGO RPCs and custom messages are the network boundary; they are NOT "events" in the ADR-010 sense — ADR-010 governs intra-process messaging only | LOW |

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

> **Engine risk gate**: ADR-004 Validation Criteria require empirical verification of `CustomMessagingManager` and `NetworkManager.ServerTime.Tick` behavior in a Unity 6.3 headless build before the Networking Core implementation sprint is greenlit. Run this check before the sprint opens.

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
| 003 | Message Envelope & Fixed-Point Primitive Serialization | Logic | Ready | ADR-004 |
| 004 | EntityID/Enum Wire-Safety Guards | Logic | Ready | ADR-004 |
| 005 | Version/SequenceNumber Stale-Discard Helpers | Logic | Ready | ADR-004 |
| 006 | Priority-Path Cap & Two-Path Delivery Model | Logic | Ready | ADR-004 |
| 007 | R-U/U-U Batch Framing, Buffer Pooling & Overflow Policy | Integration | Ready | ADR-004 |
| 008 | Heartbeat Message & IL2CPP AOT Guardrails | Logic | Ready | ADR-004 |
| 009 | Fixed 20Hz Server Tick Loop | Logic | Ready | ADR-004 |
| 010 | Cross-Cutting RPC Guards | Logic | Ready | ADR-004 |
| 011 | Commit-Before-Broadcast Generic Pattern | Logic | Ready | ADR-001 |
| 012 | Player Connection State Machine — Core Transitions | Logic | Ready | ADR-004 |
| 013 | Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth | Integration | Ready | ADR-001 + ADR-004 |
| 014 | Zone Session State Machine & Capacity Enforcement | Logic | Ready | ADR-004 |
| 015 | TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases | Integration | Ready | ADR-004 |
| 016 | Session Token Generation, Validation & Rotation (NSCRT) | Logic | Ready | None (pure crypto, no ADR applies) |
| 017 | Ghost Promotion & State Constraints | Logic | Ready | ADR-004 |
| 018 | Pre-Disconnect Snapshot & Write-Ordering | Logic | Ready | ADR-004 |
| 019 | Ghost Death & Mob De-Targeting | Logic | Ready | ADR-004 |
| 020 | Ghost Reward Forfeit Policy — Two-Pool XP & Party Slot Retention | Logic | Ready | ADR-004 |
| 021 | Ghost Cleanup, Zone Crash & Voluntary Dismissal | Integration | Ready | ADR-004 |
| 022 | LastBeatServerTick Slot Allocation & Data Structure | Logic | Ready | ADR-004 |
| 023 | OWL Wrap-Correction Compensation Formula | Logic | Ready | ADR-004 |
| 024 | OWL Threshold Suspension & Hysteresis Signal | Logic | Ready | ADR-004 |
| 025 | Message Criticality/Channel Routing Table & Unclassified-Message Fallback | Logic | Ready | ADR-004 |
| 026 | GoldSyncEvent Forced-Delivery Overflow Policy | Logic | Ready | ADR-004 |
| 027 | SelfDamageEvent vs DamageEvent Delivery Exclusivity | Integration | Ready | ADR-004 |
| 028 | EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm | Integration | Ready | ADR-004 |
| 029 | SetTarget RPC & Target Slot Management | Logic | Ready | ADR-004 |

**Scoped out of this epic** (owned by other systems' future epics, using these GDDs as their wire-contract reference): every specific downstream message schema for Auto-Attack Combat, Currency, Leveling, Zone Instancing, Party, Inventory, Equipment, NPC Shop, Consumable Use, Movement, and Skill systems. Networking Core owns the envelope/channel/tick/session/ghost/OWL/relevance-filter/test-harness substrate only.

**Known blockers/inconsistencies to resolve before final sign-off** (do not block starting implementation, but should be tracked):
- OQ-NET-1 (BLOCKING, design): `HEARTBEAT_TIMEOUT_SECONDS` production default undetermined (recommended 8-12s) — stories 012/013/017 use test-injected override values and are unaffected, but the production default must be set before launch.
- Cross-doc AC-ID collision: root `networking-core.md` and `networking-wire-protocol.md` each independently define an unrelated "AC-NC-31" (Story 024 vs. Story 004 respectively).
- Cross-doc constant inconsistency: `GHOST_COMBAT_TTL_MINUTES` (`networking-session.md`, flat 60s) vs. `GHOST_COMBAT_TTL_MIN_S` (`networking-ghost-session.md` F-GH-1, formula-based 30s baseline) — Stories 019/021 use the ghost-session formula as authoritative pending a design decision.
- `StatID` enum cross-doc dependency: Character Stats GDD's `StatID` needs `enum:uint`→`enum:byte` before Story 004 can drop its byte-transmission workaround.
- ADR-004's engine-risk profiling gate (verify `CustomMessagingManager` + `NetworkManager.ServerTime.Tick` in a real Unity 6.3 headless build) must pass before the implementation sprint is greenlit, per this epic's own "Engine risk gate" note above.

## Next Step

Run `/story-readiness [story-path]` on Story 001 to confirm implementation-readiness, then `/dev-story` to begin.
