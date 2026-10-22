FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Atlassian.Mcp.Core/Atlassian.Mcp.Core.csproj src/Atlassian.Mcp.Core/
COPY src/Atlassian.Mcp.Jira/Atlassian.Mcp.Jira.csproj src/Atlassian.Mcp.Jira/
COPY src/Atlassian.Mcp.Confluence/Atlassian.Mcp.Confluence.csproj src/Atlassian.Mcp.Confluence/
COPY src/Atlassian.Mcp.Bitbucket/Atlassian.Mcp.Bitbucket.csproj src/Atlassian.Mcp.Bitbucket/
COPY src/Atlassian.Mcp.Host/Atlassian.Mcp.Host.csproj src/Atlassian.Mcp.Host/
RUN dotnet restore src/Atlassian.Mcp.Host/Atlassian.Mcp.Host.csproj

COPY src/ src/
RUN dotnet publish src/Atlassian.Mcp.Host/Atlassian.Mcp.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ENTRYPOINT ["dotnet", "Atlassian.Mcp.Host.dll"]
