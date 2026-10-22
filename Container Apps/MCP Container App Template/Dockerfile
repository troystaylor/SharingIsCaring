FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/McpContainerApp/McpContainerApp.csproj src/McpContainerApp/
RUN dotnet restore src/McpContainerApp/McpContainerApp.csproj

COPY src/ src/
RUN dotnet publish src/McpContainerApp/McpContainerApp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ENTRYPOINT ["dotnet", "McpContainerApp.dll"]
