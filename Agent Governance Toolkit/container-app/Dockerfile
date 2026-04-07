# ========================================
# Stage 1: Build
# ========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ARG AGT_VERSION=""

# Copy project file and restore
COPY AgentGovernance.Api.csproj .
RUN dotnet restore

# Override NuGet package version if build arg is provided
RUN if [ -n "$AGT_VERSION" ]; then \
        dotnet add package Microsoft.AgentGovernance --version "$AGT_VERSION"; \
    fi

# Copy source and publish
COPY Program.cs .
COPY policies/ ./policies/
RUN dotnet publish -c Release -o /app

# ========================================
# Stage 2: Runtime
# ========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser

COPY --from=build /app .
COPY --from=build /src/policies/ ./policies/

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER appuser
ENTRYPOINT ["dotnet", "AgentGovernance.Api.dll"]
