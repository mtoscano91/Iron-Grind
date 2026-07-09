# ADR-004: Networking Library Selection — Netcode for GameObjects (NGO)

> **Status**: Accepted (2026-06-15)
> **Date**: 2026-06-15
> **Deciders**: Technical Director
> **Affected systems**: Networking Core, Networking Wire Protocol, Networking Session, Client-Side Prediction, Movement System, and all networking sub-documents that reference "library selection deferred to ADR"

---

## Context

`networking-core.md` (CR-NET-3, CR-NET-8) and `networking-wire-protocol.md` (CR-NET-7) specify a complete set of *behavioral* networking contracts — three message channels, a custom 10-byte message envelope, a two-path batched delivery model, a per-session RTT/OWL estimator, and a 20 Hz authoritative tick — but explicitly defer the choice of the concrete networking library to an ADR. The "Implementation Prerequisite — Library ADR" section of `networking-core.md` states that **no implementation of Networking Core may begin until this ADR is finalized and approved**, and enumerates exactly what it must specify: library selection rationale, channel type mapping, envelope field ownership (CR-NET-7.1), RTT measurement API (CR-NET-8.1), persistence write confirmation latency budget (OQ-NET-5), clock source, and hosting/session approach.

This ADR resolves **library selection only**. Hosting backend (self-hosted VPS vs. relay vs. a managed game-server service) and the persistence engine are explicitly deferred to separate future ADRs; this document notes how those decisions interface with NGO but does not make them.

A second forcing function already exists in committed design: the Client-Side Prediction GDD names a specific library API as its clock source. **CR-CSP-3** and **CR-CSP-21** both require that all client tick numbering derive from `NetworkManager.ServerTime.Tick` (the NGO `NetworkTickSystem`), and **EC-CSP-4** assumes "a healthy NGO clock (`NetworkManager.ServerTime`)." Selecting any library other than NGO would require rewriting these already-approved CSP rules and their acceptance criteria. This is the single strongest constraint on the decision.

---

## Decision

**The project adopts Unity Netcode for GameObjects (NGO, package `com.unity.netcode.gameobjects`) as its networking library.** NGO is used as the **connection-management and transport layer**; the project retains ownership of its own serialization, message envelope, channel routing, and batch framing as specified in `networking-wire-protocol.md`. NGO's high-level convenience layer (auto-replicated `NetworkVariable<T>`, source-generated RPC serialization) is **not** used on the hot path — see Decision 4.

### 1. Library Selection Rationale

1. **First-party, version-aligned support.** NGO ships and is versioned by Unity for the pinned engine (Unity 6.3 LTS, internal 6000.3, LTS through Dec 2027). It is the only option with guaranteed forward compatibility for the pinned engine and its security/LTS patch stream.
2. **Already locked in by approved design.** CR-CSP-3 and CR-CSP-21 specify `NetworkManager.ServerTime.Tick` (NGO `NetworkTickSystem`) as the authoritative clock. Choosing otherwise invalidates approved CSP rules and ACs.
3. **Mirror is not officially supported for Unity 6.x** and is community-maintained; its LTS/patch cadence is not aligned with the pinned engine.
4. **Photon Fusion / PUN** requires leaving Unity's built-in networking ecosystem and adopting a third-party transport and matchmaking model, increasing vendor lock-in at the transport layer.
5. **The Multiplay Hosting shutdown (2026-03-31) is library-neutral.** That shutdown affected a *hosting* product, not the NGO library, and the equivalent objection applies to every library choice. It is therefore not a reason to prefer or reject any library, and is correctly scoped to the future Hosting ADR.

### 2. Channel Type Mapping (satisfies CR-NET-3)

NGO sends carry a `NetworkDelivery` value selected per-send. The three CR-NET-3 channel types map as follows:

| CR-NET-3 Channel | Guarantees required | NGO `NetworkDelivery` (proposed) |
|------------------|---------------------|----------------------------------|
| Reliable Ordered (R-OD) | Guaranteed, in-order per sender, dedup | `ReliableSequenced` |
| Reliable Unordered (R-U) | Guaranteed, no ordering, dedup | `Reliable` |
| Unreliable (U-U) | Best-effort, no retransmit, no ordering | `Unreliable` |

Notes:
- Bulk transfer messages (`MessageTypeID` `0xF000–0xFFFF`, e.g. `ZoneStateSnapshot`) that exceed the transport MTU and require fragmentation must use the fragmenting reliable-ordered delivery (`ReliableFragmentedSequenced` in current NGO/Unity Transport). The project's own sub-message framing (CR-NET-7) handles batching *within* the 512-byte cap; transport-level fragmentation is reserved for the connection-event bulk messages only.
- **Exact `NetworkDelivery` enum member names are subject to verification against the Unity 6.3 NGO / Unity Transport API.** The *guarantee mapping* above is the binding contract; the specific enum identifiers must be confirmed against `docs/engine-reference/unity` before code is written. NGO's per-send delivery selection is exposed through the custom messaging API (Decision 4), not through `NetworkVariable`.

### 3. RTT Measurement API (satisfies CR-NET-8.1)

CR-NET-8.1 requires a per-session OWL estimate derived from an application-level `RttProbe` message (U-U, outside the batch) that the client echoes "immediately with no processing delay," with `OWL = round_trip_time / 2`, and an initial estimate seeded from the session-handshake exchange timing.

**Decision:** The application-level `RttProbe`/echo defined in CR-NET-8.1 is the **authoritative** OWL source. It is implemented over the custom messaging path (Decision 4) on the U-U channel, sent every `RTT_PROBE_INTERVAL_SECONDS`. The reason it is authoritative — rather than reusing a transport metric — is that OWL compensation must reflect the *application-perceived* round trip (the path the `NotifySkillUsed` RPC actually travels), and the GDD mandates a client echo with no processing delay precisely so the measurement is independent of transport internals.

NGO's transport additionally exposes a transport-layer RTT. This **may be used only to seed the initial OWL estimate at handshake** and as a cross-check/diagnostic — it does not replace the `RttProbe` measurement. **The exact method name and return type are subject to verification against the Unity 6.3 NGO API** and must be confirmed before implementation; the binding contract is "seed-only, not authoritative," independent of the precise signature.

### 4. Envelope Field Ownership (satisfies CR-NET-7.1)

The project's 10-byte envelope (`MessageTypeID` `ushort`, `SequenceNumber` `uint`, `ServerTickNumber` `uint`; +`SenderEntityID` `uint` for client→server) and its batch framing (the 12-byte batch header, `0x0100–0x01FF` range) are **owned by the project, not by NGO**. To preserve that ownership:

1. Hot-path and gameplay messages are sent through NGO's **custom messaging API** (`CustomMessagingManager` — named/unnamed messages) or an equivalent low-level send, where the project's serialized envelope + payload is the message body. The `NetworkDelivery` value (Decision 2) is supplied per send.
2. NGO's own framing — connection ID, its internal message headers, and transport-channel selection — is **disjoint** from the project envelope and wraps it. The two layers never share fields:
   - NGO owns: connection/client identity (`ulong clientId`), transport channel selection, its own reliability/sequencing headers.
   - Project owns: `MessageTypeID`, `SequenceNumber` (per-connection, RFC-1982 stale-discard per CR-NET-7.5), `ServerTickNumber`, `SenderEntityID`.
3. `SenderEntityID` is a **project EntityID, not NGO's `clientId`.** The server maintains the `clientId ↔ EntityID` mapping; client-reported `SenderEntityID` is validated against that mapping (and against the EntityID-validity and session-ready gates in `networking-core.md` Cross-Cutting Constraints).
4. **`NetworkVariable<T>` and source-generated RPC serialization are not used for authoritative gameplay state.** They impose their own delta/replication framing and ordering model that conflicts with CR-NET-7's explicit wire contract. NGO is the connection/transport substrate; serialization stays project-owned. NGO's RPC attributes may still be used for non-hot-path, low-frequency control flows where convenient, provided they do not carry authoritative state governed by CR-NET-7.

### 5. Clock Source (satisfies CR-NET-2, CR-CSP-3, CR-CSP-21)

`NetworkManager.ServerTime.Tick` (the NGO `NetworkTickSystem`) is the **authoritative tick counter** for all tick-numbered operations, including the `ServerTickNumber` envelope field (CR-NET-7.1), `MovementIntentMessage.TickNumber` (CR-CSP-3), and `LastBeatServerTick` (CR-NET-2). NGO's `NetworkConfig.TickRate` **must be configured to `TICK_RATE_HZ` = 20** so the NetworkTickSystem cadence matches CR-NET-2's fixed 50 ms server tick. This usage is already committed in the approved CSP GDD and is confirmed correct. EC-CSP-4's "> 3 tick offset" desync handling relies on NGO's `ServerTime` internals and is unaffected by this ADR.

### 6. Persistence Write Confirmation Latency Budget (OQ-NET-5)

OQ-NET-5 (commit-before-broadcast write latency budget, CR-NET-5) is **out of scope for this ADR** and remains BLOCKING against the future Persistence ADR. Note for that ADR: the budget must keep the total enhancement round-trip within `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` (default 200 ms), of which NGO transport RTT is one component; the persistence write path must fit inside the remainder after transport RTT and server validation. NGO does not constrain this choice — it is a server-side persistence concern.

---

## Consequences

### Positive
- Approved CSP rules (CR-CSP-3/21, EC-CSP-4) require no rewrite — NGO is their assumed clock source.
- First-party LTS support aligned with the pinned engine through Dec 2027; security and compatibility patches arrive with the engine.
- NGO's connection lifecycle callbacks (`OnClientConnectedCallback`, `OnClientDisconnectCallback`) map directly onto the connection-driven execution category in CR-NET-2 and the session lifecycle in `networking-session.md`.
- Retaining project-owned serialization (Decision 4) means the CR-NET-7 wire contract, bandwidth formulas (F-NET-1–7), and the test harness remain library-independent — a future library swap would not touch the wire format.

### Negative / Trade-offs accepted
- We are **deliberately bypassing NGO's headline convenience features** (`NetworkVariable`, generated RPCs) on the hot path. This means more custom code via `CustomMessagingManager` and less benefit from NGO's "batteries-included" model — but it is the only way to honor the CR-NET-7 wire contract.
- We are adopting NGO **against a standing recommendation in our own engine reference** (Mirror for small MMOs). That reference is updated alongside this ADR; see Risks.
- NGO's design center is session/instanced multiplayer rather than seamless persistent worlds; we are relying on Iron Grind's instanced-zone model (10-50 players per zone) keeping us inside that design center.

### Neutral
- Hosting backend and persistence engine remain open (separate ADRs). NGO works with self-hosted dedicated servers and with relay/managed services, so this decision does not pre-commit hosting.

---

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| NGO underperforms at instanced-zone scale (50 players + 150 mobs in the tick loop, F-NET-9) | Medium | High | The F-NET-9 / OQ-NC-SER-3 profiling pass must add the n=50 + m=150 all-`COMBAT_ACTIVE` scenario and confirm tick stays < 30 ms on target server hardware *before* Networking Core implementation is greenlit. |
| `NetworkDelivery` enum names / transport RTT signature differ in Unity 6.3 (post-cutoff API) | Medium | Low | Decisions 2 & 3 bind the *guarantees*, not the identifiers. Verify exact API against `docs/engine-reference/unity` before coding; this ADR is re-validated on any engine upgrade. |
| Bypassing `NetworkVariable`/RPC codegen reduces NGO ecosystem/tooling leverage | Medium | Low | Document the `CustomMessagingManager` pattern as the project standard. |

---

## Performance Implications

| Metric | Budget | Note |
|--------|--------|------|
| Server tick | < 30 ms at n=50+m=150 (F-NET-9) | NGO tick overhead must be measured inside this budget; profiling gate above. |
| Per-client batch | ≤ 512-byte body (F-NET-6) | Unchanged — project owns framing; NGO carries the body. |
| Client memory | ≤ 1.5 GB ceiling (technical-preferences.md) | NGO client runtime footprint must be confirmed during integration. |

---

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | Future **Hosting Backend ADR** (VPS / relay / managed service); future **Persistence Layer ADR** (resolves OQ-NET-5) |
| **Blocks** | Networking Core implementation, Wire Protocol implementation, Client-Side Prediction implementation, Movement System networking — all gated on this ADR per `networking-core.md` Implementation Prerequisite |
| **Ordering Note** | Library selection (this ADR) must be Accepted before the Hosting and Persistence ADRs. |

---

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (internal 6000.3) |
| **Domain** | Networking |
| **Knowledge Risk** | HIGH — NGO API changed across Unity 6.0–6.3, beyond LLM training cutoff |
| **Post-Cutoff APIs Used** | `NetworkManager.ServerTime.Tick` / `NetworkTickSystem` (confirmed via CSP GDD and engine reference); `NetworkConfig.TickRate`; `CustomMessagingManager`; `NetworkDelivery` enum; transport RTT (seed-only) |
| **Verification Required** | (1) Exact `NetworkDelivery` enum member names for the three-channel mapping; (2) transport RTT API signature/return type; (3) `CustomMessagingManager` send API for per-send `NetworkDelivery` selection; (4) confirm `NetworkConfig.TickRate` accepts 20 Hz |

> Knowledge Risk is HIGH — this ADR must be re-validated if the project upgrades engine versions.

---

## GDD Requirements Addressed

| GDD Document | Requirement | How This ADR Satisfies It |
|--------------|-------------|----------------------------|
| `networking-core.md` | CR-NET-3 — three channel types must be implemented or mapped | Decision 2 maps R-OD/R-U/U-U to NGO `NetworkDelivery` guarantees |
| `networking-core.md` | CR-NET-8.1 — per-session RTT/OWL via `RttProbe` | Decision 3 makes the application-level `RttProbe` authoritative; transport RTT seeds only |
| `networking-core.md` | CR-NET-2 — fixed 20 Hz authoritative tick | Decision 5 sets `NetworkConfig.TickRate` = 20, clock = `NetworkManager.ServerTime.Tick` |
| `networking-core.md` | Implementation Prerequisite — Library ADR | This ADR resolves library selection; hosting/persistence deferred per the prerequisite |
| `networking-wire-protocol.md` | CR-NET-7.1 — custom message envelope | Decision 4 establishes envelope field ownership; project owns serialization over NGO custom messaging |
| `client-side-prediction.md` | CR-CSP-3, CR-CSP-21 — clock = `NetworkManager.ServerTime.Tick` | Decision 5 adopts NGO `NetworkTickSystem` as the committed clock source |

---

## Alternatives Considered

### Alternative 1: Mirror (rejected)

- **Description**: Open-source (MIT) community successor to UNet, previously recommended by this project's `docs/engine-reference/unity/current-best-practices.md` for small-scale MMOs.
- **Pros**: Battle-tested for traditional MMO patterns; the engine reference (at the time of authoring) favored it for authoritative-server + character-persistence models; larger MMO-architecture community.
- **Cons**: Not officially supported on Unity 6.x; patch cadence not aligned with the pinned LTS; **adopting it would require rewriting approved CSP rules CR-CSP-3/CR-CSP-21/EC-CSP-4**, which name the NGO clock API directly.
- **Rejection Reason**: The committed CSP clock-source lock-in plus the lack of official Unity 6.x support outweighs Mirror's MMO-pattern maturity. The best-practices recommendation predates the CSP clock commitment and assumes a *persistent-world* topology; Iron Grind's *instanced 10-50 player zones* are session-shaped, which narrows Mirror's advantage. `current-best-practices.md` has been updated to point to this ADR.

### Alternative 2: Photon Fusion / PUN (rejected)

- **Description**: Third-party networking + matchmaking middleware with its own transport.
- **Cons**: Transport-layer vendor lock-in; its replication model would still require bypassing to honor CR-NET-7; same CSP-rewrite cost as Mirror.
- **Rejection Reason**: Higher lock-in and ecosystem departure with no offsetting benefit.

### Alternative 3: Custom low-level transport (rejected)

- **Description**: Build directly on Unity Transport (UTP) / raw sockets, no high-level library.
- **Cons**: Re-implements connection management, tick system, and lifecycle that NGO provides; loses `NetworkManager.ServerTime`; large unbudgeted engineering cost.
- **Rejection Reason**: The project retains serialization ownership anyway (Decision 4); fully custom adds connection/clock re-implementation cost with no wire-contract benefit.

---

## Open Questions

**OQ-ADR4-1 — Hosting backend (deferred to Hosting ADR):** self-hosted dedicated NGO server vs. relay vs. managed game-server hosting. Multiplay Hosting is not an option (shut down 2026-03-31).

**OQ-ADR4-2 — Persistence write latency (OQ-NET-5, deferred to Persistence ADR):** the commit-before-broadcast write budget within `ENHANCEMENT_PROCESS_LATENCY_MAX_MS`.

**OQ-ADR4-3 — `CustomMessagingManager` vs. thin custom transport wrapper:** whether to route project messages through NGO's `CustomMessagingManager` or a thinner abstraction over UTP while still using NGO for connection/clock. Resolve during Networking Core implementation spike after API verification items above are confirmed.
