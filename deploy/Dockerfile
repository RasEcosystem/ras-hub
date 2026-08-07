FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

COPY Directory.Build.props version.json RasHub.sln ./
COPY src/RasHub.Application/RasHub.Application.csproj src/RasHub.Application/
COPY src/RasHub.Domain/RasHub.Domain.csproj src/RasHub.Domain/
COPY src/RasHub.Infrastructure/RasHub.Infrastructure.csproj src/RasHub.Infrastructure/
COPY src/RasHub.Web/RasHub.Web.csproj src/RasHub.Web/
COPY src/RasHub.Synchronization/RasHub.Synchronization.csproj src/RasHub.Synchronization/
COPY src/RasHub.Contracts/src/RasHub.Contracts/RasHub.Contracts.csproj src/RasHub.Contracts/src/RasHub.Contracts/

RUN dotnet restore src/RasHub.Web/RasHub.Web.csproj -m:1

COPY . .

RUN dotnet publish src/RasHub.Web/RasHub.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -m:1 \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

WORKDIR /app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER app

HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=5 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "RasHub.Web.dll"]
