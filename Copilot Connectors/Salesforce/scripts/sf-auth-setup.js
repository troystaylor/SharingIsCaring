#!/usr/bin/env node

/**
 * Salesforce OAuth Setup Script
 *
 * One-time script to authenticate with Salesforce via the SF CLI,
 * extract the refresh token, and write credentials to .env.local.
 *
 * Prerequisites:
 *   - Salesforce CLI installed: https://developer.salesforce.com/tools/salesforcecli
 *   - A Salesforce Connected App with OAuth enabled
 *
 * Usage:
 *   node scripts/sf-auth-setup.js --instance-url https://yourorg.my.salesforce.com --client-id YOUR_CLIENT_ID
 *
 * What it does:
 *   1. Opens your browser for Salesforce login via `sf org login web`
 *   2. After you approve, extracts the access token, refresh token, and instance URL
 *   3. Writes/updates SF_ variables in .env.local
 */

const { execSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ENV_FILE = path.resolve(__dirname, "..", ".env.local");
const ALIAS = "copilot-connector";

function parseArgs() {
  const args = process.argv.slice(2);
  const parsed = {};
  for (let i = 0; i < args.length; i++) {
    if (args[i] === "--instance-url" && args[i + 1]) {
      parsed.instanceUrl = args[i + 1];
      i++;
    } else if (args[i] === "--client-id" && args[i + 1]) {
      parsed.clientId = args[i + 1];
      i++;
    } else if (args[i] === "--alias" && args[i + 1]) {
      parsed.alias = args[i + 1];
      i++;
    } else if (args[i] === "--help" || args[i] === "-h") {
      printUsage();
      process.exit(0);
    }
  }
  return parsed;
}

function printUsage() {
  console.log(`
Salesforce OAuth Setup Script

Usage:
  node scripts/sf-auth-setup.js --instance-url <url> --client-id <id>

Options:
  --instance-url  Salesforce org URL (e.g., https://yourorg.my.salesforce.com)
  --client-id     Connected App consumer key
  --alias         SF CLI org alias (default: copilot-connector)
  --help, -h      Show this help message

Example:
  node scripts/sf-auth-setup.js \\
    --instance-url https://mycompany.my.salesforce.com \\
    --client-id 3MVG9IHf89I1t8hrvswazsWedXWY0i...
`);
}

function checkSfCli() {
  try {
    execSync("sf --version", { stdio: "pipe" });
  } catch {
    console.error(
      "Error: Salesforce CLI (sf) is not installed or not in PATH."
    );
    console.error(
      "Install it from: https://developer.salesforce.com/tools/salesforcecli"
    );
    process.exit(1);
  }
}

function loginToSalesforce(instanceUrl, clientId, alias) {
  console.log("\nOpening browser for Salesforce login...");
  console.log("Log in and approve access when prompted.\n");

  const cmd = [
    "sf org login web",
    `--instance-url ${instanceUrl}`,
    `--alias ${alias}`,
  ];

  if (clientId) {
    cmd.push(`--client-id ${clientId}`);
  }

  try {
    execSync(cmd.join(" "), { stdio: "inherit" });
  } catch {
    console.error("Error: Salesforce login failed.");
    process.exit(1);
  }
}

function extractOrgInfo(alias) {
  console.log("\nExtracting org credentials...");

  let result;
  try {
    result = execSync(`sf org display --target-org ${alias} --json`, {
      stdio: "pipe",
      encoding: "utf-8",
    });
  } catch {
    console.error("Error: Could not retrieve org info. Is the org authorized?");
    process.exit(1);
  }

  const parsed = JSON.parse(result);
  const org = parsed.result;

  return {
    instanceUrl: org.instanceUrl,
    accessToken: org.accessToken,
    username: org.username,
    orgId: org.id,
  };
}

function extractRefreshToken(alias) {
  // The SF CLI stores auth info including refresh tokens in its auth files.
  // We can retrieve it via `sf org display` with verbose output, or from the
  // auth file directly. The `sf org list auth` command can also help.
  //
  // The most reliable approach: read from the SFDX auth file.
  const homeDir = process.env.USERPROFILE || process.env.HOME;
  const sfdxDir = path.join(homeDir, ".sfdx");

  // Try to find the auth file for this alias/username
  let orgInfo;
  try {
    const result = execSync(
      `sf org display --target-org ${alias} --json --verbose`,
      {
        stdio: "pipe",
        encoding: "utf-8",
      }
    );
    orgInfo = JSON.parse(result).result;
  } catch {
    return null;
  }

  // Check if the SF CLI stores auth files (location varies by CLI version)
  const possiblePaths = [
    path.join(homeDir, ".sf", "org", `${orgInfo.username}.json`),
    path.join(sfdxDir, `${orgInfo.username}.json`),
  ];

  for (const authPath of possiblePaths) {
    if (fs.existsSync(authPath)) {
      try {
        const authData = JSON.parse(fs.readFileSync(authPath, "utf-8"));
        if (authData.refreshToken) {
          return authData.refreshToken;
        }
      } catch {
        // Continue to next path
      }
    }
  }

  return null;
}

function updateEnvFile(vars) {
  let content = "";

  if (fs.existsSync(ENV_FILE)) {
    content = fs.readFileSync(ENV_FILE, "utf-8");
  }

  for (const [key, value] of Object.entries(vars)) {
    if (!value) continue;

    const regex = new RegExp(`^${key}=.*$`, "m");
    if (regex.test(content)) {
      content = content.replace(regex, `${key}=${value}`);
    } else {
      content += `${content.endsWith("\n") || content === "" ? "" : "\n"}${key}=${value}\n`;
    }
  }

  fs.writeFileSync(ENV_FILE, content, "utf-8");
}

async function main() {
  const args = parseArgs();

  if (!args.instanceUrl) {
    console.error("Error: --instance-url is required");
    printUsage();
    process.exit(1);
  }

  const alias = args.alias || ALIAS;

  // Step 1: Verify SF CLI is installed
  checkSfCli();

  // Step 2: Browser-based login
  loginToSalesforce(args.instanceUrl, args.clientId, alias);

  // Step 3: Extract org credentials
  const orgInfo = extractOrgInfo(alias);
  console.log(`  Instance URL: ${orgInfo.instanceUrl}`);
  console.log(`  Username:     ${orgInfo.username}`);
  console.log(`  Org ID:       ${orgInfo.orgId}`);

  // Step 4: Extract refresh token from SF CLI auth files
  const refreshToken = extractRefreshToken(alias);

  if (refreshToken) {
    console.log("  Refresh Token: [extracted]");
  } else {
    console.warn(
      "\n  Warning: Could not extract refresh token from SF CLI auth files."
    );
    console.warn(
      "  You may need to extract it manually from ~/.sf/ or ~/.sfdx/ auth files."
    );
  }

  // Step 5: Write to .env.local
  const envVars = {
    SF_INSTANCE_URL: orgInfo.instanceUrl,
    SF_ACCESS_TOKEN: orgInfo.accessToken,
  };

  if (args.clientId) {
    envVars.SF_CLIENT_ID = args.clientId;
  }

  if (refreshToken) {
    envVars.SF_REFRESH_TOKEN = refreshToken;
  }

  updateEnvFile(envVars);

  console.log(`\n.env.local updated at: ${ENV_FILE}`);
  console.log("\nVariables written:");
  for (const key of Object.keys(envVars)) {
    console.log(`  ${key}=${key.includes("TOKEN") || key.includes("SECRET") ? "[redacted]" : envVars[key]}`);
  }

  if (!refreshToken) {
    console.log(`
Next step: Manually add SF_REFRESH_TOKEN to .env.local.
Check these locations for the auth file:
  - ~/.sf/org/<username>.json
  - ~/.sfdx/<username>.json
Look for the "refreshToken" field.
`);
  }

  console.log("\nDone! Your connector can now use the Refresh Token Flow");
  console.log("to obtain fresh access tokens for each crawl.");
}

main().catch((err) => {
  console.error("Unexpected error:", err.message);
  process.exit(1);
});
