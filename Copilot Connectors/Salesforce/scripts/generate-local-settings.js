#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

const envFile = path.resolve(__dirname, "..", ".env.local");
const settingsFile = path.resolve(__dirname, "..", "local.settings.json");

function readEnvFile(filePath) {
  const content = fs.readFileSync(filePath, "utf-8");
  const values = {};

  for (const line of content.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) {
      continue;
    }

    const eqIndex = trimmed.indexOf("=");
    if (eqIndex <= 0) {
      continue;
    }

    const key = trimmed.slice(0, eqIndex).trim();
    const value = trimmed.slice(eqIndex + 1).trim();
    values[key] = value;
  }

  return values;
}

if (!fs.existsSync(envFile)) {
  console.error("Missing .env.local. Create it from .env.local.template first.");
  process.exit(1);
}

const envValues = readEnvFile(envFile);
const settings = {
  IsEncrypted: false,
  Values: {
    AzureWebJobsStorage: "",
    FUNCTIONS_WORKER_RUNTIME: "node",
    FUNCTIONS_NODE_BLOCK_ON_ENTRY_POINT_ERROR: "true",
    ...envValues,
  },
};

fs.writeFileSync(settingsFile, `${JSON.stringify(settings, null, 2)}\n`, "utf-8");
console.log(`Wrote ${settingsFile}`);