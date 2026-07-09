FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV NODE_MAJOR=22

# System deps
RUN apt-get update && apt-get install -y \
    curl \
    ca-certificates \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# Node.js 22.x
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Playwright with Chromium only (skip Firefox/WebKit to save space)
RUN npm install -g playwright@latest \
    && npx playwright install chromium --with-deps \
    && rm -rf /root/.npm /tmp/*

# Create workspace directory
RUN mkdir -p /workspace && chmod 777 /workspace

# Verify installation
RUN node --version && npx playwright --version

WORKDIR /workspace
