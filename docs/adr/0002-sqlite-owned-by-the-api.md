# 2. SQLite, owned by the API alone

**Status:** accepted · 2026-10-31

## Context
Jobs live minutes: from upload to delivered callback. There are few of them and they need no reporting.

## Decision
- SQLite through EF Core, migrated at API start, in WAL mode.
- Only the API reads and writes it. Workers report results through RabbitMQ events and never see the database, so there is exactly one writer process and no shared-file locking across containers.

## Consequences
- The API is a single instance. Running several replicas would require a server database (the EF Core model would move unchanged).
- Status transitions are idempotent (`Queued` → `Completed`/`Failed` only), which makes redelivered events harmless.
