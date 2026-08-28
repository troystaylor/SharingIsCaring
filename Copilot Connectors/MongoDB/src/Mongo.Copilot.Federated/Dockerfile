FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Mongo.Copilot.Federated/Mongo.Copilot.Federated.csproj", "src/Mongo.Copilot.Federated/"]
COPY ["src/Mongo.Copilot.Core/Mongo.Copilot.Core.csproj", "src/Mongo.Copilot.Core/"]
RUN dotnet restore "src/Mongo.Copilot.Federated/Mongo.Copilot.Federated.csproj"
COPY . .
RUN dotnet publish "src/Mongo.Copilot.Federated/Mongo.Copilot.Federated.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Collection profiles are mounted or baked in at deploy time.
COPY ["config/", "/app/config/"]
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Mongo.Copilot.Federated.dll"]
