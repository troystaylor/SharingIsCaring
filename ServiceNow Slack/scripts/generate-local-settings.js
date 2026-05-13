#!/usr/bin/env node

/**
 * Generate local.settings.json from .env.local for Azure Functions Core Tools.
 */

const fs = require("fs");
const path = require("path");

const envFile = path.resolve(__dirname, "..", ".env.local");
const outFile = path.resolve(__dirname, "..", "local.settings.json");

if (!fs.existsSync(envFile)) {
  console.error(".env.local not found. Copy .env.local.template to .env.local first.");
  process.exit(1);
}

const lines = fs.readFileSync(envFile, "utf-8").split("\n");
const values = { FUNCTIONS_WORKER_RUNTIME: "node", AzureWebJobsStorage: "" };

for (const line of lines) {
  const trimmed = line.trim();
  if (!trimmed || trimmed.startsWith("#")) continue;
  const eqIdx = trimmed.indexOf("=");
  if (eqIdx === -1) continue;
  const key = trimmed.substring(0, eqIdx).trim();
  const val = trimmed.substring(eqIdx + 1).trim();
  if (key && val) values[key] = val;
}

const settings = {
  IsEncrypted: false,
  Values: values,
};

fs.writeFileSync(outFile, JSON.stringify(settings, null, 2));
console.log(`Generated ${outFile}`);
