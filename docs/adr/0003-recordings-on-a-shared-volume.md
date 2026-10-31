# 3. Recordings on a shared volume, not in messages

**Status:** accepted · 2026-10-31

## Context
A 30-minute call is up to 100 MB. Large RabbitMQ messages hurt broker memory and throughput.

## Decision
The API stores the upload as `/data/audio/{jobId}.wav` on a volume mounted read-only into the workers; the message carries the path. The API deletes the recording as soon as the result arrives; retention removes anything left behind (failed uploads, missed deletions).

## Consequences
API and workers must share storage (a Docker volume here; a network file system or object storage in a cluster).
