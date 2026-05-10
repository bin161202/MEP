# syntax=docker/dockerfile:1.6
# Build context: repo root (d:\MEP Add-in\MEP\MEPAuto\)
# Build cmd: docker build -f src/server/MEPAuto.Server.Api/Dockerfile -t mepauto-api:latest .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIG=Release
WORKDIR /src

# Copy build infrastructure
COPY Directory.Build.props Directory.Packages.props nuget.config* ./
COPY MEPAuto.sln ./

# Copy server projects + contracts (KHÔNG copy client để tránh cần Revit DLL)
COPY shared/MEPAuto.Contracts/ ./shared/MEPAuto.Contracts/
COPY src/server/ ./src/server/

# Restore + publish chỉ MEPAuto.Server.Api (Directory.Build.props sẽ chỉ load Server/Contracts projects)
RUN dotnet restore src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj
RUN dotnet publish src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj \
    -c $BUILD_CONFIG \
    --no-restore \
    -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install curl cho HEALTHCHECK (aspnet:8.0 base không có wget/curl).
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Tạo non-root user
RUN useradd -m -u 1000 mepauto && \
    mkdir -p /var/mepauto-data && chown -R mepauto:mepauto /var/mepauto-data
USER mepauto

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Healthcheck (docker compose / k8s sử dụng)
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --max-time 3 http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MEPAuto.Server.Api.dll"]
