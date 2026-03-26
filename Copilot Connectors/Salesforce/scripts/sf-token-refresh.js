#!/usr/bin/env node

/**
 * Salesforce OAuth Token Refresh Script
 *
 * Starts a local HTTP server on port 8080, opens the browser for Salesforce login,
 * captures the authorization code callback, exchanges it for tokens, and updates .env.local.
 */

const http = require("http");
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const ENV_FILE = path.resolve(__dirname, "..", ".env.local");
const PORT = 8080;
const REDIRECT_URI = `http://localhost:${PORT}/callback`;

// Read current .env.local for SF credentials
function readEnvFile() {
  const content = fs.readFileSync(ENV_FILE, "utf-8");
  const vars = {};
  for (const line of content.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const eqIndex = trimmed.indexOf("=");
    if (eqIndex > 0) {
      vars[trimmed.substring(0, eqIndex)] = trimmed.substring(eqIndex + 1);
    }
  }

  // Normalize SF_INSTANCE_URL so users can store just the instance name
  if (vars.SF_INSTANCE_URL) {
    let url = vars.SF_INSTANCE_URL.trim();
    if (!/^https?:\/\//i.test(url)) {
      url = /\.salesforce\.com$/i.test(url)
        ? `https://${url}`
        : `https://${url}.my.salesforce.com`;
    }
    vars.SF_INSTANCE_URL = url.replace(/\/+$/, "");
  }

  return vars;
}

function updateEnvVar(key, value) {
  let content = fs.readFileSync(ENV_FILE, "utf-8");
  const regex = new RegExp(`^${key}=.*$`, "m");
  if (regex.test(content)) {
    content = content.replace(regex, `${key}=${value}`);
  } else {
    content += `\n${key}=${value}\n`;
  }
  fs.writeFileSync(ENV_FILE, content, "utf-8");
}

async function exchangeCodeForTokens(code, env) {
  const tokenUrl = `${env.SF_INSTANCE_URL}/services/oauth2/token`;
  console.log(`  Token endpoint: ${tokenUrl}`);

  const params = new URLSearchParams({
    grant_type: "authorization_code",
    code,
    client_id: env.SF_CLIENT_ID,
    client_secret: env.SF_CLIENT_SECRET,
    redirect_uri: REDIRECT_URI,
  });

  let response;
  try {
    response = await fetch(tokenUrl, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: params.toString(),
    });
  } catch (err) {
    throw new Error(
      `Could not reach ${tokenUrl} — ${err.message}\n` +
      `  Check that SF_INSTANCE_URL is correct in .env.local.\n` +
      `  Current value: ${env.SF_INSTANCE_URL}`
    );
  }

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(`Token exchange failed (${response.status}): ${errorBody}`);
  }

  return response.json();
}

function main() {
  const env = readEnvFile();

  if (!env.SF_CLIENT_ID || !env.SF_INSTANCE_URL) {
    console.error("Error: SF_CLIENT_ID and SF_INSTANCE_URL must be set in .env.local");
    process.exit(1);
  }

  console.log(`\nUsing SF_INSTANCE_URL: ${env.SF_INSTANCE_URL}`);

  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://localhost:${PORT}`);

    if (url.pathname === "/callback") {
      const code = url.searchParams.get("code");
      const error = url.searchParams.get("error");

      if (error) {
        res.writeHead(400, { "Content-Type": "text/html" });
        res.end(`<h2>OAuth Error</h2><p>${error}: ${url.searchParams.get("error_description") || ""}</p>`);
        server.close();
        process.exit(1);
      }

      if (!code) {
        res.writeHead(400, { "Content-Type": "text/html" });
        res.end("<h2>Error</h2><p>No authorization code received.</p>");
        return;
      }

      try {
        console.log("\nAuthorization code received. Exchanging for tokens...");
        const tokens = await exchangeCodeForTokens(code, env);

        // Update .env.local
        updateEnvVar("SF_REFRESH_TOKEN", tokens.refresh_token);
        if (tokens.instance_url) {
          updateEnvVar("SF_INSTANCE_URL", tokens.instance_url);
        }

        console.log("\n  Refresh Token: [updated in .env.local]");
        console.log(`  Instance URL:  ${tokens.instance_url}`);
        console.log("\nDone! Restart the Functions host to use the new token.\n");

        res.writeHead(200, { "Content-Type": "text/html" });
        res.end("<h2>Success!</h2><p>Tokens updated in .env.local. You can close this tab.</p>");
      } catch (err) {
        console.error(`\nToken exchange failed: ${err.message}`);
        res.writeHead(500, { "Content-Type": "text/html" });
        res.end(`<h2>Token Exchange Failed</h2><p>${err.message}</p>`);
      }

      setTimeout(() => { server.close(); process.exit(0); }, 1000);
    } else {
      res.writeHead(404);
      res.end("Not found");
    }
  });

  server.listen(PORT, () => {
    const authUrl = [
      `${env.SF_INSTANCE_URL}/services/oauth2/authorize`,
      `?response_type=code`,
      `&client_id=${encodeURIComponent(env.SF_CLIENT_ID)}`,
      `&redirect_uri=${encodeURIComponent(REDIRECT_URI)}`,
      `&scope=api+refresh_token`,
    ].join("");

    console.log(`\nListening on http://localhost:${PORT}/callback`);
    console.log("Opening browser for Salesforce login...\n");

    // Open browser
    try {
      execSync(`start "" "${authUrl}"`, { stdio: "ignore", shell: true });
    } catch {
      console.log(`Open this URL manually:\n${authUrl}\n`);
    }
  });
}

main();
