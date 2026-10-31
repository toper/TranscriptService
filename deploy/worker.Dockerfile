# syntax=docker/dockerfile:1
# One image definition for every engine worker. The engine is a build argument, the device is the target:
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=whispernet --target runtime-cpu  -t transcriptservice-whispernet:cpu  .
#   docker build -f deploy/worker.Dockerfile --build-arg WORKER=whispernet --target runtime-cuda -t transcriptservice-whispernet:cuda .
# WORKER is the engine name (whispernet, fasterwhisper, vosk), matching the compose profiles. The CUDA
# base image follows from it (cuda-base-<WORKER> below), so callers never pass it.

ARG WORKER

# ---- Build: restore from project files only (cached until a project or package version changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS projects
WORKDIR /src
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY --parents src/*/*.csproj ./

FROM projects AS publish-cpu
ARG WORKER
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "$(find src -iname "TranscriptService.Worker.${WORKER}.csproj")" --runtime linux-x64 -p:UseCuda=false
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "$(find src -iname "TranscriptService.Worker.${WORKER}.csproj")" \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore \
        --output /app -p:UseAppHost=false -p:UseCuda=false

FROM projects AS publish-cuda
ARG WORKER
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "$(find src -iname "TranscriptService.Worker.${WORKER}.csproj")" --runtime linux-x64 -p:UseCuda=true
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "$(find src -iname "TranscriptService.Worker.${WORKER}.csproj")" \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore \
        --output /app -p:UseAppHost=false -p:UseCuda=true

# ---- CUDA base per engine: the CUDA/cuDNN its native build was compiled against.
# Whisper.net 1.9: CUDA 13 (driver >= 580).
FROM nvidia/cuda:13.0.3-runtime-ubuntu24.04 AS cuda-base-whispernet
# FasterWhisper.NET 1.0.8: CUDA 12 + cuDNN 8 (kernels up to sm_120).
FROM nvidia/cuda:12.8.1-runtime-ubuntu22.04 AS cuda-base-fasterwhisper
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libcudnn8 \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dotnet

# ---- CPU: Ubuntu-based ASP.NET runtime plus OpenMP and OpenBLAS, which the native engines use.
FROM dotnet AS runtime-cpu
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 libopenblas0 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish-cpu /app .
# /app goes to the dynamic loader, whose ldconfig also creates the soname links native libraries
# expect (FasterWhisper.NET 1.0.8 ships libctranslate2.so while its wrapper asks for .so.4).
# "worker" starts the published entry assembly, the one with a runtimeconfig.
RUN echo /app > /etc/ld.so.conf.d/transcriptservice.conf && ldconfig \
    && printf '#!/bin/sh\nexec dotnet /app/%s.dll "$@"\n' "$(basename /app/*.runtimeconfig.json .runtimeconfig.json)" > /usr/local/bin/worker \
    && chmod 755 /usr/local/bin/worker
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cpu
USER $APP_UID
EXPOSE 8080
# Loading a large model takes a while; readiness flips once it is in memory.
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=3 CMD ["worker", "--health-probe"]
ENTRYPOINT ["worker"]

# ---- CUDA: the engine's NVIDIA base image with the .NET runtime copied in.
FROM cuda-base-${WORKER} AS runtime-cuda
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid app --no-create-home --shell /usr/sbin/nologin app
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
WORKDIR /app
COPY --from=publish-cuda /app .
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && echo /app > /etc/ld.so.conf.d/transcriptservice.conf && ldconfig \
    && printf '#!/bin/sh\nexec dotnet /app/%s.dll "$@"\n' "$(basename /app/*.runtimeconfig.json .runtimeconfig.json)" > /usr/local/bin/worker \
    && chmod 755 /usr/local/bin/worker
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
