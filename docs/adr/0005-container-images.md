# 5. Container images

**Status:** accepted · 2026-10-31

## Decision
- Multi-stage builds: restore runs in its own layer from project files only, with a BuildKit cache for NuGet packages; the runtime stage carries no SDK. Images are published for `linux-x64` only.
- API: `aspnet:10.0-noble-chiseled` — no shell, no package manager, non-root. Health checks run the app itself with `--health-probe`, because the image has no curl.
- Workers: `aspnet:10.0-noble` for CPU (whisper.cpp needs OpenMP), `nvidia/cuda:13.0.3-runtime-ubuntu24.04` with the .NET runtime copied in for CUDA. Both run as a non-root `app` user. The device is a build argument; a CUDA image built without the CUDA runtime fails at build time.
- Models are never baked into images: a `models-init` container downloads and checksums them into a volume mounted read-only by the worker.

## Consequences
CUDA images are large (~4.7 GB, mostly the NVIDIA base) and are built, but not run, in CI.
