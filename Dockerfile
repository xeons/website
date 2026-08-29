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

RUN dotnet restore src/XeonProductions.Web/XeonProductions.Web.csproj -r linux-x64 \
    && dotnet restore tools/XeonProductions.WpImporter/XeonProductions.WpImporter.csproj -r linux-x64

COPY . .

# Deliberately not --no-restore. The restore above runs against the project files alone, for
# layer caching, and that leaves an obj/ which a --no-restore publish will happily reuse.
# Doing so silently drops the framework's own static web assets, blazor.web.js among them,
# which breaks every interactive component with no build error to show for it. The package
# cache from the layer above is still reused, so re-running restore here costs very little.
#
# -r linux-x64 names the only platform this image runs on. Without it the publish carries a
# runtimes/ directory for every identifier NuGet knows about, nineteen of them, which is
# most of the size of the result. --self-contained false keeps the framework itself out,
# since the runtime image already has it.
RUN dotnet publish src/XeonProductions.Web/XeonProductions.Web.csproj \
    -c Release \
    -r linux-x64 --self-contained false \
    -o /publish \
    /p:UseAppHost=false

# The importer ships alongside the app so a migration can be run in the deployed environment,
# against the same database and the same media volume. It goes in its own folder: publishing
# both to one directory would have the two appsettings.json files overwrite each other.
RUN dotnet publish tools/XeonProductions.WpImporter/XeonProductions.WpImporter.csproj \
    -c Release \
    -r linux-x64 --self-contained false \
    -o /publish-importer \
    /p:UseAppHost=false

# Moved aside so it can be copied into a layer of its own below. A directory cannot be both
# copied separately and left in place, because the copy that follows would carry it again
# and put it back in the same layer.
#
# There is no runtimes/ to separate: naming a single identifier above puts this platform's
# native libraries straight into the output root instead.
RUN mkdir -p /layers && mv /publish/wwwroot /layers/wwwroot

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp's native library links against fontconfig; curl is used by the healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

# Three layers rather than one, ordered by how often each changes. A layer is stored and
# transferred whole, so a deploy that only touches code re-sends only the last of these.
COPY --from=build /publish-importer ./importer
COPY --from=build /layers/wwwroot ./wwwroot
COPY --from=build /publish .

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
