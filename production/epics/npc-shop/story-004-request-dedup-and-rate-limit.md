# Story 004: Repeat-Request Cache and Rate Limit

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` CR-SHOP-5 note; ADR-001 Decisions 1 and 5, Amendment A1
**Requirement**: `TR-shop-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001: Purchase Transaction Integrity
**ADR Decision Summary**: Deduplicate on `(charId, messageType, requestId)` within `SESSION_TTL_SECONDS`; a duplicate returns the cached result without re-processing; 10 Buy/Sell requests per second per character, excess → `RejectedRateLimited`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: `messageType` taken from the envelope, never hardcoded (A1); dedup window 300 s
- Guardrail: bounded memory per character; no per-request allocation on the hit path

---

## Acceptance Criteria

- [ ] **AC-1 Hit**: after a request `(c, T, r)` completes with a result, a second `(c, T, r)` within 300 s returns the same encoded result (same message type and body) and the handler is not run again — no gold, inventory or store change.
- [ ] **AC-2 Key parts**: `(c, Buy, 1)` and `(c, Sell, 1)` are distinct entries; `(c1, Buy, 1)` and `(c2, Buy, 1)` are distinct.
- [ ] **AC-3 In flight**: a duplicate of a request still in progress (Buy waiting on `BeginPurchase`) is not run again and gets no reply; the client receives the one result when it completes.
- [ ] **AC-4 Window and reset**: an entry older than 300 s is forgotten (the request runs again); a successful `OpenNPCInteraction` clears that character's shop entries (the client counter restarts at 0 — epic open item 7), using Story 002's open hook. Rejections are cached the same as successes.
- [ ] **AC-5 Rate limit**: an 11th Buy/Sell from one character within any 1 s window → `RejectedRateLimited(requestId)`, not processed and not cached; another character is unaffected; after the window slides, requests pass again.
- [ ] **AC-6 Bound**: per-character entries are capped (e.g. 32, oldest evicted, one warning when evicting an entry younger than the window); entries are dropped when the character's session ends.

---

## Implementation Notes

- A `ShopRequestCache` in `IronGrind.NpcShop` keyed by character, holding `(messageType, requestId) → (state: InFlight | Done, cached result bytes, time)`. Time from the same injected clock as the session tracker.
- Rate limit: the guard chain (`CrossCuttingRpcGuardChain`) uses a per-type minimum tick gap; 10 per sliding second is not a gap. Decide at readiness: a dedicated sliding-window counter here (ADR-001 Decision 5 wording) or a 2-tick gap via `RpcTypeTag` (stricter, simpler). Record the decision.
- Duplicate replies go to the owning client through the outbox, same path as the original.

---

## Out of Scope

- Stories 005 / 006: the handlers that call this cache

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: run a counting handler through the cache twice with the same key → handler count 1, two identical replies.
- **AC-2**: the three pairs → handler count 2 each.
- **AC-3**: begin key, duplicate before completion → handler count 1, replies 0; complete → replies 1.
- **AC-4**: clock +299.9 → hit; +300 → re-run; open hook → re-run at once; rejection cached.
- **AC-5**: 10 requests in 0.9 s → all processed; 11th → `RejectedRateLimited`, handler not called; other character 10 more → processed; clock +1 s → passes.
- **AC-6**: 33 distinct ids → first evicted; session end → entries gone.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_RequestCache_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002 (open hook), 003 (`RejectedRateLimited` codec)
- Unlocks: Stories 005, 006
