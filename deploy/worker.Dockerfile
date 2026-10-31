# syntax=docker/dockerfile:1
# One image definition for every engine worker. The engine is a build argument, the device is the target:
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=WhisperNet --target runtime-cpu  -t transcriptservice-whispernet:cpu  .
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=WhisperNet --target runtime-cuda -t transcriptservice-whispernet:cuda .
# Engines built against another CUDA/cuDNN pass CUDA_IMAGE and CUDA_PACKAGES (see deploy/docker-compose.gpu.yml).

ARG WORKER
# CUDA 13 runtime (driver >= 580), what Whisper.net needs.
ARG CUDA_IMAGE=nvidia/cuda:13.0.3-runtime-ubuntu24.04

# ---- Build: restore from project files only (cached until a project or package version changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS projects
WORKDIR /src
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY --parents src/*/*.csproj ./

FROM projects AS publish-cpu
ARG WORKER
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TranscriptService.Worker.${WORKER}/TranscriptService.Worker.${WORKER}.csproj --runtime linux-x64 -p:UseCuda=false
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TranscriptService.Worker.${WORKER}/TranscriptService.Worker.${WORKER}.csproj \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore \
        --output /app -p:UseAppHost=false -p:UseCuda=false

FROM projects AS publish-cuda
ARG WORKER
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TranscriptService.Worker.${WORKER}/TranscriptService.Worker.${WORKER}.csproj --runtime linux-x64 -p:UseCuda=true
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TranscriptService.Worker.${WORKER}/TranscriptService.Worker.${WORKER}.csproj \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore \
        --output /app -p:UseAppHost=false -p:UseCuda=true

# ---- Common runtime settings. Workers run as the non-root "app" user (uid 1654) on port 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dotnet

# ---- CPU: Ubuntu-based ASP.NET runtime plus OpenMP and OpenBLAS, which the native engines use.
FROM dotnet AS runtime-cpu
ARG WORKER
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 libopenblas0 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish-cpu /app .
# FasterWhisper.NET 1.0.8 ships libctranslate2.so, but its native wrapper loads it by soname (.so.4).
RUN if [ -f libctranslate2.so ]; then ln -s libctranslate2.so libctranslate2.so.4; fi
ENV WORKER_ASSEMBLY=TranscriptService.Worker.${WORKER}.dll \
    LD_LIBRARY_PATH=/app \
    ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cpu
USER $APP_UID
EXPOSE 8080
# Loading a large model takes a while; readiness flips once it is in memory.
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 \
    CMD dotnet "$WORKER_ASSEMBLY" --health-probe
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$WORKER_ASSEMBLY\" \"$@\"", "worker"]

# ---- CUDA: NVIDIA runtime image with the .NET runtime copied in.
FROM ${CUDA_IMAGE} AS runtime-cuda
ARG WORKER
ARG CUDA_PACKAGES=""
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 ca-certificates ${CUDA_PACKAGES} \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid app --no-create-home --shell /usr/sbin/nologin app
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
WORKDIR /app
COPY --from=publish-cuda /app .
RUN if [ -f libctranslate2.so ]; then ln -s libctranslate2.so libctranslate2.so.4; fi
ENV WORKER_ASSEMBLY=TranscriptService.Worker.${WORKER}.dll \
    LD_LIBRARY_PATH=/app \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cuda \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 \
    CMD dotnet "$WORKER_ASSEMBLY" --health-probe
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$WORKER_ASSEMBLY\" \"$@\"", "worker"]
