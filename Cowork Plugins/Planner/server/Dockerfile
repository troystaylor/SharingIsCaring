FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY PlannerCoworkMcp.csproj ./
RUN dotnet restore PlannerCoworkMcp.csproj

COPY . .
RUN dotnet publish PlannerCoworkMcp.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PlannerCoworkMcp.dll"]
