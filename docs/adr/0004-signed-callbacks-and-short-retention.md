# 4. Signed callbacks and short retention

**Status:** accepted · 2026-10-31

## Context
Clients should not have to poll, and transcripts of phone calls should not be kept longer than needed.

## Decision
- Clients may pass `callbackUrl`. The result is POSTed with `X-Signature: sha256=<HMAC-SHA256 of the body>` (shared secret) and `X-Transcription-Id`; `CallbackSigner` in Contracts lets receivers verify it.
- Delivery uses the standard resilience pipeline (retries with backoff, per-attempt and total timeouts), with one pipeline per receiver so a failing one cannot open the circuit for others. A delivered job is deleted immediately; undelivered results after retries are kept until the TTL.
- Callbacks to loopback, private and link-local addresses are refused unless explicitly allowed (SSRF protection; the check runs on the resolved address).
- Results without a callback expire after 15 minutes; jobs no worker finished are failed and the client notified.

## Consequences
Clients that cannot receive callbacks must fetch results within the TTL.
