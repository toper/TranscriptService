# syntax=docker/dockerfile:1
# One image definition for every engine worker. The engine is a build argument, the device is the target:
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=whispernet --target runtime-cpu  -t transcriptservice-whispernet:cpu  .
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=whispernet --target runtime-cuda -t transcriptservice-whispernet:cuda .
# WORKER is the engine name (whispernet, fasterwhisper, vosk), as in the compose profiles. Each engine
# has a cpu-base-<WORKER> and, if it runs on GPU, a cuda-base-<WORKER> stage with its native dependencies.

ARG WORKER

# ---- Build: restore from project files only (cached until a project or package version changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS projects
ARG WORKER
WORKDIR /src
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY --parents src/*/*.csproj ./
# Project directories are PascalCase (TranscriptService.Worker.WhisperNet); WORKER is the engine name.
RUN project="$(find src -iname "TranscriptService.Worker.${WORKER}.csproj")" \
    && test -n "$project" || { echo "No worker project for WORKER=${WORKER}" >&2; exit 1; } \
    && echo "$project" > /src/worker-project

FROM projects AS publish-cpu
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "$(cat worker-project)" --runtime linux-x64 -p:UseCuda=false
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "$(cat worker-project)" --configuration Release --runtime linux-x64 --self-contained false \
        --no-restore --output /app -p:UseAppHost=false -p:UseCuda=false

FROM projects AS publish-cuda
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "$(cat worker-project)" --runtime linux-x64 -p:UseCuda=true
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "$(cat worker-project)" --configuration Release --runtime linux-x64 --self-contained false \
        --no-restore --output /app -p:UseAppHost=false -p:UseCuda=true

# ---- Native dependencies per engine.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dotnet

# Whisper.net (whisper.cpp): OpenMP on CPU; CUDA 13 on GPU (driver >= 580).
FROM dotnet AS cpu-base-whispernet
RUN apt-get update && apt-get install --yes --no-install-recommends libgomp1 && rm -rf /var/lib/apt/lists/*
FROM nvidia/cuda:13.0.3-runtime-ubuntu24.04 AS cuda-base-whispernet
RUN apt-get update && apt-get install --yes --no-install-recommends libgomp1 && rm -rf /var/lib/apt/lists/*

# FasterWhisper.NET (CTranslate2): OpenMP and OpenBLAS on CPU; CUDA 12 + cuDNN 8 on GPU (kernels up to sm_120).
FROM dotnet AS cpu-base-fasterwhisper
RUN apt-get update && apt-get install --yes --no-install-recommends libgomp1 libopenblas0 && rm -rf /var/lib/apt/lists/*
FROM nvidia/cuda:12.8.1-runtime-ubuntu22.04 AS cuda-base-fasterwhisper
RUN apt-get update && apt-get install --yes --no-install-recommends libgomp1 libcudnn8 && rm -rf /var/lib/apt/lists/*

# ---- CPU image. Workers run as the non-root "app" user (uid 1654) on port 8080.
FROM cpu-base-${WORKER} AS runtime-cpu
WORKDIR /app
COPY --from=publish-cpu /app .
COPY --chmod=755 deploy/worker.sh /usr/local/bin/worker
# Native libraries live in /app; ldconfig also creates the soname links they expect
# (FasterWhisper.NET 1.0.8 ships libctranslate2.so while its wrapper asks for .so.4).
RUN echo /app > /etc/ld.so.conf.d/transcriptservice.conf && ldconfig
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cpu
USER $APP_UID
EXPOSE 8080
# Loading a large model takes a while; readiness flips once it is in memory.
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 CMD ["worker", "--health-probe"]
ENTRYPOINT ["worker"]

# ---- CUDA image: the engine's NVIDIA base with the .NET runtime copied in.
FROM cuda-base-${WORKER} AS runtime-cuda
RUN groupadd --gid 1654 app && useradd --uid 1654 --gid app --no-create-home --shell /usr/sbin/nologin app
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
WORKDIR /app
COPY --from=publish-cuda /app .
COPY --chmod=755 deploy/worker.sh /usr/local/bin/worker
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && echo /app > /etc/ld.so.conf.d/transcriptservice.conf && ldconfig
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cuda \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 CMD ["worker", "--health-probe"]
ENTRYPOINT ["worker"]
