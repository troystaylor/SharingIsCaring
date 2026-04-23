FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DecisionDuck.McpServer.csproj ./
RUN dotnet restore DecisionDuck.McpServer.csproj

COPY . .
RUN dotnet publish DecisionDuck.McpServer.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DecisionDuck.McpServer.dll"]
