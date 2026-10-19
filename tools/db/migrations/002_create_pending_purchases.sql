-- 002_create_pending_purchases.sql
-- The PendingPurchase table of ADR-006 "Key Interfaces" (Decision 3; character-persistence.md CR-CP-12),
-- applied by hand after 001 (see tools/db/README.md). Character Persistence Story 013.
-- Requires 001: the foreign key references character_records. Not re-runnable.
--
-- state maps to PendingPurchaseState: GoldDebited = 0, Refunded = 1, Completed = 2 (ADR-001 Decision 2).

CREATE TABLE pending_purchases (
    id          BIGSERIAL    PRIMARY KEY,
    char_id     BIGINT       NOT NULL REFERENCES character_records(character_id),
    request_id  BIGINT       NOT NULL,
    item_id     BIGINT       NOT NULL,
    quantity    INTEGER      NOT NULL,
    total_cost  BIGINT       NOT NULL,
    state       SMALLINT     NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_pending_purchases_char_id ON pending_purchases(char_id);
