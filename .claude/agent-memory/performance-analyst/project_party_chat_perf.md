---
name: project-party-chat-perf
description: Party Chat GDD adversarial review Pass 1 — 3 BLOCKING items; AC-PC-6 test trap, missing per-connection rate limit, CR-PC-4 algorithm gap
metadata:
  type: project
---

Party Chat GDD (`design/gdd/party-chat.md`) adversarial review Pass 1 (2026-06-19). Document status: Pending Review.

**3 BLOCKING, 4 ADVISORY.**

**Why:** Chat is a new document with low cross-system touch surface but concentrated validation and anti-abuse logic that requires precise specification.

**How to apply:** All 3 BLOCKING items must be resolved before implementation epics are opened. AC-PC-6 fix is highest priority — it drives incorrect implementation if unresolved.

## BLOCKING

**Finding 1 — CR-PC-4 sliding-window algorithm unspecified**
"Sliding-window rate counter" is named but no algorithm is given. A fixed-window implementation is a silent spec violation enabling 7.5× bandwidth burst (10 msgs in 2s at max message size) at window seams. Fix: specify ring buffer of N=RATE_MAX timestamps; message permitted when oldest timestamp > W_s seconds old.

**Finding 4 — No per-connection inbound rate limit on PartyChatRequest**
CR-PC-4 throttle fires only after deserialization + session check + content validation + solo check. Malformed packets (EC-PC-10 byte-cap, CR-PC-3 encoding failures) bypass the throttle entirely. No per-connection rate limit is specified for PartyChatRequest. Compare: BuyRequest/SellRequest/UseItemRequest all have explicit "10 req/sec" limits in the CCR routing table. Fix: add CR-PC-7 (PARTY_CHAT_INBOUND_RATE_MAX, suggested 20/sec, discard before deserialization); add to CCR routing table.

**Finding 7 — AC-PC-6 conflates byte-cap silence with codepoint rejection; AC-PC-7 misattributes anomaly log**
AC-PC-6 expects PartyChatRejected with reason=MessageTooLong(3) for a 385-byte payload. EC-PC-10 explicitly says NO PartyChatRejected is sent for byte-cap violations — the server silently discards. The test will always timeout/fail and incentivizes an engineer to emit PartyChatRejected for byte violations, contradicting EC-PC-10.
MessageTooLong(3) is for codepoint overflow (>128 codepoints), not byte overflow.
Fix: split AC-PC-6 into AC-PC-6a (385B → silence + ChatMalformedPayload log) and AC-PC-6b (129 ASCII codepoints → PartyChatRejected reason=3).
AC-PC-7: note says "logged as ChatMalformedPayload" — wrong. ChatMalformedPayload is EC-PC-10's log (byte cap violation). Invalid UTF-8 in textBytes is CR-PC-3 check 2, which logs nothing defined. Remove the log attribution from AC-PC-7.

## ADVISORY

**Finding 2 — Counter lifecycle: memory is negligible (2KB/zone); zone-exit eviction unspecified**
At 50 players × 40B per ring buffer = 2KB per zone — no concern. But zone-exit eviction is not specified; if counter is in a global structure it may outlive the zone. Fix: one sentence pinning counter scope to zone instance + lazy eviction when all timestamps > W_s old.

**Finding 3 — Amplification 4.3× but 41 KB/s zone-wide maximum — negligible**
Per-party server egress at max load: 4 × 5 × 428 × 4 / 10 = 3,424 bytes/s. Zone-wide max (~12 parties): ~41 KB/s. Amplification 4.28×. Negligible vs tick batch. F-PC-1 only gives per-recipient incoming rate; per-zone egress total is absent. Fix: add zone-level note to F-PC-1.

**Finding 5 — PRIORITY_PATH_CAP interaction undocumented**
PartyChatMessage is non-exempt R-OD and counts toward PRIORITY_PATH_CAP=8. 4 simultaneous chat sends per member = 4 slots (half the cap). At cap minimum (4), chat alone fills the cap; concurrent P1 events defer +50ms. GDD does not acknowledge this. Fix: add note to CR-PC-1 step 7.

**Finding 6 — F-PC-1 formula correct; N_party variable description misleads**
Formula is exact (echo makes sender a source to itself, N_party=4 is correct, not N_party-1). Non-sending member gets 642 B/s; sending member gets 856 B/s. The variable description "Active sending members" without explaining echo semantics risks a 25% under-sized implementation. Fix: add echo explanation to N_party description.

## Cross-doc gaps already flagged in OQ-PC-1 (OPEN)
Wire protocol amendments for PartyChatRequest/Message/Rejected/Throttled not yet registered in networking-wire-protocol.md or networking-channel-contract.md CCR-3 routing table. Implementation blocker per the GDD itself.
