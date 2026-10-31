# syntax=docker/dockerfile:1
# Build from the repository root, choosing the device:
#   docker build -f src/TranscriptService.Worker.WhisperNet/Dockerfile --target runtime-cpu  -t transcriptservice-whispernet:cpu .
#   docker build -f src/TranscriptService.Worker.WhisperNet/Dockerfile --target runtime-cuda --build-arg COMPUTE_DEVICE=cuda -t transcriptservice-whispernet:cuda .

ARG COMPUTE_DEVICE=cpu

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG COMPUTE_DEVICE
WORKDIR /src

# Restore in its own layer: it only re-runs when project files or package versions change.
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/TranscriptService.Contracts/TranscriptService.Contracts.csproj src/TranscriptService.Contracts/
COPY src/TranscriptService.Audio/TranscriptService.Audio.csproj src/TranscriptService.Audio/
COPY src/TranscriptService.ServiceDefaults/TranscriptService.ServiceDefaults.csproj src/TranscriptService.ServiceDefaults/
COPY src/TranscriptService.Worker.Core/TranscriptService.Worker.Core.csproj src/TranscriptService.Worker.Core/
COPY src/TranscriptService.Worker.WhisperNet/TranscriptService.Worker.WhisperNet.csproj src/TranscriptService.Worker.WhisperNet/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TranscriptService.Worker.WhisperNet/TranscriptService.Worker.WhisperNet.csproj \
        --runtime linux-x64 -p:ComputeDevice=${COMPUTE_DEVICE}

COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TranscriptService.Worker.WhisperNet/TranscriptService.Worker.WhisperNet.csproj \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore \
        --output /app -p:UseAppHost=false -p:ComputeDevice=${COMPUTE_DEVICE}

# ---- CPU: Ubuntu-based ASP.NET runtime plus the OpenMP library whisper.cpp uses for its threads.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime-cpu
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cpu
EXPOSE 8080
# Loading a large model takes a while; readiness flips once it is in memory.
HEALTHCHECK --interval=15s --timeout=5s --start-period=180s --retries=3 \
    CMD ["dotnet", "TranscriptService.Worker.WhisperNet.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "TranscriptService.Worker.WhisperNet.dll"]

# ---- CUDA: NVIDIA runtime image (CUDA 13, driver >= 580) with the .NET runtime copied in.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dotnet-runtime

FROM nvidia/cuda:13.0.3-runtime-ubuntu24.04 AS runtime-cuda
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgomp1 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid app --no-create-home --shell /usr/sbin/nologin app
COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
WORKDIR /app
COPY --from=build /app .
# Fail the build, not the deployment, when the CUDA runtime was not published (missing build arg).
RUN test -n "$(find /app -name 'libggml-cuda-whisper.so' -print -quit)" \
    || (echo "CUDA runtime missing: build with --build-arg COMPUTE_DEVICE=cuda" >&2 && exit 1)
USER app
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    ASPNETCORE_HTTP_PORTS=8080 \
    Engine__Device=Cuda \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=180s --retries=3 \
    CMD ["dotnet", "TranscriptService.Worker.WhisperNet.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "TranscriptService.Worker.WhisperNet.dll"]
