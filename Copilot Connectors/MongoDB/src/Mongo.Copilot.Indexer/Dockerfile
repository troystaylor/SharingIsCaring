FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Mongo.Copilot.Indexer/Mongo.Copilot.Indexer.csproj", "src/Mongo.Copilot.Indexer/"]
COPY ["src/Mongo.Copilot.Core/Mongo.Copilot.Core.csproj", "src/Mongo.Copilot.Core/"]
RUN dotnet restore "src/Mongo.Copilot.Indexer/Mongo.Copilot.Indexer.csproj"
COPY . .
RUN dotnet publish "src/Mongo.Copilot.Indexer/Mongo.Copilot.Indexer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# The indexer reads the same profiles as the federated head, which is what keeps
# indexed and live fields from drifting apart.
COPY ["config/", "/app/config/"]
ENTRYPOINT ["dotnet", "Mongo.Copilot.Indexer.dll"]
