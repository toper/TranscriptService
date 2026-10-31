# 1. Messaging with EasyNetQ over RabbitMQ

**Status:** accepted · 2026-10-31

## Context
The API must hand recordings to engine workers that may be busy, restarting or loading a model for minutes. Exactly one engine kind is deployed at a time, chosen by the operator.

## Decision
- One durable work queue, `transcription.requests`, consumed by whichever worker runs (competing consumers). Results come back as `TranscriptionCompleted` / `TranscriptionFailed` events via publish/subscribe.
- EasyNetQ instead of the raw client or MassTransit: typed messages and sensible defaults with little ceremony.
- The queue is a priority queue (`x-max-priority = 2`): recordings up to 30 s overtake long calls, so live demo utterances are not stuck behind an hour of audio. Workers prefetch one message, otherwise a buffered job could not be overtaken.
- The queue's arguments live in one place (`MessagingTopology`); the API declares the queue before sending, so requests made before any worker starts are kept, not dropped by the broker.

## Consequences
- Scaling means adding worker replicas, not raising prefetch.
- A long CPU transcription may exceed RabbitMQ's default acknowledgement timeout; `consumer_timeout` is raised to 2 h in `deploy/rabbitmq/rabbitmq.conf`.
