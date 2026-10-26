-- 003_pending_purchases_request_unique.sql
-- Makes BeginPurchase idempotent per request: one pending purchase per (character, request id)
-- (ADR-001 Amendment A2 addendum; ADR-006 Decision 3; Character Persistence Story 015).
-- Applied by hand after 002 (see tools/db/README.md). Requires 002: the index is on pending_purchases.
-- Not re-runnable.

CREATE UNIQUE INDEX ux_pending_purchases_char_request ON pending_purchases (char_id, request_id);
