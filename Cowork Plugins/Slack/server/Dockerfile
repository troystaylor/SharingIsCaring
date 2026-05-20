# syntax=docker/dockerfile:1.7
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY SlackCoworkMcp.csproj ./
RUN dotnet restore SlackCoworkMcp.csproj

COPY . .
RUN dotnet publish SlackCoworkMcp.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# .NET 8+ aspnet images ship with a built-in non-root `app` user (uid 1654 in
# 10.0). Reuse it instead of recreating, then run as it.
RUN chown -R app:app /app
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=1

COPY --from=build --chown=app:app /app ./

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "SlackCoworkMcp.dll"]
