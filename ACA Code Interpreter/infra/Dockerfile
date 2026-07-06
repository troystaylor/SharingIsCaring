# ACA Code Interpreter - Custom Disk Image
# Pre-baked with all 16 language/mode runtimes for zero cold-start execution
FROM ubuntu:24.04

LABEL maintainer="Troy Taylor <troy@troystaylor.com>"
LABEL description="ACA Sandboxes disk image for Code Interpreter connector"

ENV DEBIAN_FRONTEND=noninteractive
ENV WORKSPACE=/workspace

# ─── System packages ───────────────────────────────────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gnupg \
    software-properties-common \
    git \
    unzip \
    jq \
    sqlite3 \
    ruby \
    perl \
    php-cli \
    python3 \
    python3-pip \
    python3-venv \
    libxml2-dev \
    libxslt1-dev \
    && rm -rf /var/lib/apt/lists/*

# ─── Node.js 20 LTS ───────────────────────────────────────────────────────────
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# ─── PowerShell 7.4+ ──────────────────────────────────────────────────────────
RUN curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y powershell \
    && rm -rf /var/lib/apt/lists/*

# ─── .NET 9 Runtime — NOT needed (tools are self-contained) ───────────────────
# The pfx and expr-eval binaries are published as self-contained single-file
# executables, so no .NET runtime install is required.

# ─── Python data science packages ─────────────────────────────────────────────
RUN pip3 install --no-cache-dir --break-system-packages \
    pandas \
    numpy \
    polars \
    matplotlib \
    scikit-learn \
    lxml \
    jinja2 \
    pyyaml \
    openpyxl

# ─── Node.js global packages ──────────────────────────────────────────────────
RUN npm install -g \
    tsx \
    adaptivecards \
    @stoplight/spectral-cli \
    ajv-cli \
    && npm cache clean --force

# ─── /opt/tools/ — Custom utilities for Copilot Studio maker modes ─────────────
RUN mkdir -p /opt/tools

# Adaptive Card Validator
COPY tools/validate-card.js /opt/tools/validate-card.js

# FetchXML Evaluator
COPY tools/fetchxml_eval.py /opt/tools/fetchxml_eval.py

# OpenAPI Linter
COPY tools/lint-openapi.js /opt/tools/lint-openapi.js

# Prompt Template Renderer
COPY tools/render_prompt.py /opt/tools/render_prompt.py

# Power Automate Expression Evaluator (.NET published output)
COPY tools/expr-eval/publish/ /opt/tools/
RUN chmod +x /opt/tools/expr-eval

# Regex Tester
COPY tools/regex_test.py /opt/tools/regex_test.py

# Power Fx CLI (.NET published output)
COPY tools/pfx/publish/ /opt/tools/pfx/
RUN ln -s /opt/tools/pfx/pfx /usr/local/bin/pfx && chmod +x /opt/tools/pfx/pfx

# ─── Workspace directory ───────────────────────────────────────────────────────
RUN mkdir -p $WORKSPACE
WORKDIR $WORKSPACE

# ─── Default entrypoint (sandbox exec overrides this) ──────────────────────────
CMD ["sleep", "infinity"]
