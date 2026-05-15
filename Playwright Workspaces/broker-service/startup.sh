#!/bin/bash
# Install Chromium dependencies for Playwright local mode on App Service Linux
npx playwright-core install --with-deps chromium 2>/dev/null || true
node dist/index.js
