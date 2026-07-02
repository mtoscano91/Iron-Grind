# Epic: Networking Core

> **Layer**: Foundation
> **GDD**: design/gdd/networking-core.md + 9 sub-contracts
> **Architecture Module**: Networking Core
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories networking-core`

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

## Next Step

Run `/create-stories networking-core` to break this epic into implementable stories.
