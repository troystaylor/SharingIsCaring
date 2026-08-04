# Build context is the parent folder, not this one. The project links
# ../graph-capability-index.json as an embedded resource, so that file has to be
# inside the context or the build cannot see it.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The index is copied before restore because the project references it by path.
COPY graph-capability-index.json ./

# Sits at the context root, one level above the project, which is where MSBuild
# walks up to find it.
COPY Directory.Build.props ./

# JSON array form throughout: the project folder name contains spaces.
COPY ["Graph Mission Control MCP/GraphMissionControl.Mcp.csproj", "Graph Mission Control MCP/"]
RUN dotnet restore "Graph Mission Control MCP/GraphMissionControl.Mcp.csproj"

COPY ["Graph Mission Control MCP/", "Graph Mission Control MCP/"]
RUN dotnet publish "Graph Mission Control MCP/GraphMissionControl.Mcp.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Container Apps ingress targets 8080.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Non-root. The base image ships this user.
USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GraphMissionControl.Mcp.dll"]
