# ========================================
# Stage 1: Build
# ========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ARG AGT_VERSION=""

# Copy project file, NuGet config, and any locally-built ACS nupkgs, then restore
COPY AgentGovernance.Api.csproj .
COPY NuGet.config* ./
COPY local-packages/ ./local-packages/
RUN dotnet restore

# Override NuGet package version if build arg is provided
RUN if [ -n "$AGT_VERSION" ]; then \
        dotnet add package Microsoft.AgentGovernance --version "$AGT_VERSION"; \
    fi

# Copy source and publish
COPY Program.cs .
COPY policies/ ./policies/
COPY manifests/ ./manifests/
RUN dotnet publish -c Release -o /app

# ========================================
# Stage 2: Runtime
# ========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install OPA (used by ACS Rego policies). Harmless when ACS is disabled or
# manifests use only test/cedar policies.
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL -o /usr/local/bin/opa https://openpolicyagent.org/downloads/v0.68.0/opa_linux_amd64_static \
    && chmod +x /usr/local/bin/opa \
    && apt-get purge -y curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser

COPY --from=build /app .
COPY --from=build /src/policies/ ./policies/
COPY --from=build /src/manifests/ ./manifests/

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV MANIFEST_DIR=/app/manifests
ENV ACS_OPA_PATH=/usr/local/bin/opa
EXPOSE 8080

USER appuser
ENTRYPOINT ["dotnet", "AgentGovernance.Api.dll"]
