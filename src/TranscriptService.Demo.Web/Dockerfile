# syntax=docker/dockerfile:1
# Build from the repository root:
#   docker build -f src/TranscriptService.Demo.Web/Dockerfile -t transcriptservice-demo .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore in its own layer: it only re-runs when project files or package versions change.
# No runtime identifier: it would flow into the WebAssembly client project, which targets browser-wasm.
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY --parents src/*/*.csproj ./
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/TranscriptService.Demo.Web/TranscriptService.Demo.Web.csproj
COPY src/ src/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/TranscriptService.Demo.Web/TranscriptService.Demo.Web.csproj \
        --configuration Release --no-restore --output /app /p:UseAppHost=false

# Chiseled: no shell, no package manager, non-root by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "TranscriptService.Demo.Web.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "TranscriptService.Demo.Web.dll"]
