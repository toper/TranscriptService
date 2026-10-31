# syntax=docker/dockerfile:1
# Build from the repository root:
#   docker build -f src/TranscriptService.Api/Dockerfile -t transcriptservice-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer: it only re-runs when project files or package versions change.
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY --parents src/*/*.csproj ./
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TranscriptService.Api/TranscriptService.Api.csproj --runtime linux-x64

COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TranscriptService.Api/TranscriptService.Api.csproj \
        --configuration Release --runtime linux-x64 --self-contained false --no-restore --output /app /p:UseAppHost=false

# The data volume is initialised from the image, so it must already belong to the runtime user.
RUN mkdir -p /data/audio && chown -R 1654:1654 /data

# Chiseled: no shell, no package manager, non-root by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=1654:1654 /data /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "TranscriptService.Api.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "TranscriptService.Api.dll"]
