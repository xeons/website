# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files alone, so a source-only change reuses the package layer.
COPY XeonProductions.slnx ./
COPY src/XeonProductions.Domain/*.csproj src/XeonProductions.Domain/
COPY src/XeonProductions.Infrastructure/*.csproj src/XeonProductions.Infrastructure/
COPY src/XeonProductions.Web/*.csproj src/XeonProductions.Web/
COPY tools/XeonProductions.WpImporter/*.csproj tools/XeonProductions.WpImporter/

RUN dotnet restore src/XeonProductions.Web/XeonProductions.Web.csproj \
    && dotnet restore tools/XeonProductions.WpImporter/XeonProductions.WpImporter.csproj

COPY . .

# Deliberately not --no-restore. The restore above runs against the project files alone, for
# layer caching, and that leaves an obj/ which a --no-restore publish will happily reuse.
# Doing so silently drops the framework's own static web assets, blazor.web.js among them,
# which breaks every interactive component with no build error to show for it. The package
# cache from the layer above is still reused, so re-running restore here costs very little.
RUN dotnet publish src/XeonProductions.Web/XeonProductions.Web.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# The importer ships alongside the app so a migration can be run in the deployed environment,
# against the same database and the same media volume. It goes in its own folder: publishing
# both to one directory would have the two appsettings.json files overwrite each other.
RUN dotnet publish tools/XeonProductions.WpImporter/XeonProductions.WpImporter.csproj \
    -c Release \
    -o /app/publish/importer \
    /p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp's native library links against fontconfig; curl is used by the healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Uploads, downloads and the data protection key ring live on volumes, so a rebuild takes
# neither the files nor everyone's session with it. Downloads get a volume of their own,
# outside the media root, because they are never served as static files.
RUN mkdir -p /app/media /app/downloads /app/keys \
    && chown -R app:app /app/media /app/downloads /app/keys
VOLUME ["/app/media", "/app/downloads", "/app/keys"]

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "XeonProductions.Web.dll"]
