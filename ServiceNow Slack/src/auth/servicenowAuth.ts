// ── ServiceNow OAuth Authentication ──
// Supports client_credentials and password grant flows.

import { getConfig } from "../config/connectorConfig";

interface TokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  refresh_token?: string;
  scope?: string;
}

interface CachedToken {
  accessToken: string;
  expiresAt: number;
}

const TOKEN_REFRESH_BUFFER_MS = 60 * 1000; // Refresh 60s before expiry
let cachedToken: CachedToken | null = null;

async function authenticateClientCredentials(): Promise<CachedToken> {
  const config = getConfig().servicenow;

  const params = new URLSearchParams({
    grant_type: "client_credentials",
    client_id: config.clientId,
    client_secret: config.clientSecret,
  });

  const response = await fetch(
    `${config.instanceUrl}/oauth_token.do`,
    {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: params.toString(),
    }
  );

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `ServiceNow client_credentials auth failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as TokenResponse;

  cachedToken = {
    accessToken: data.access_token,
    expiresAt: Date.now() + data.expires_in * 1000,
  };

  console.log("[Auth] Client credentials token obtained");
  return cachedToken;
}

async function authenticatePassword(): Promise<CachedToken> {
  const config = getConfig().servicenow;

  const params = new URLSearchParams({
    grant_type: "password",
    client_id: config.clientId,
    client_secret: config.clientSecret,
    username: config.username,
    password: config.password,
  });

  const response = await fetch(
    `${config.instanceUrl}/oauth_token.do`,
    {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: params.toString(),
    }
  );

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `ServiceNow password auth failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as TokenResponse;

  cachedToken = {
    accessToken: data.access_token,
    expiresAt: Date.now() + data.expires_in * 1000,
  };

  console.log("[Auth] Password grant token obtained");
  return cachedToken;
}

function isTokenExpired(): boolean {
  if (!cachedToken) return true;
  return Date.now() >= cachedToken.expiresAt - TOKEN_REFRESH_BUFFER_MS;
}

export async function refreshAccessToken(): Promise<CachedToken> {
  const config = getConfig().servicenow;

  if (config.authFlow === "client_credentials") {
    return authenticateClientCredentials();
  }
  return authenticatePassword();
}

export async function getAccessToken(): Promise<string> {
  if (isTokenExpired()) {
    await refreshAccessToken();
  }
  return cachedToken!.accessToken;
}
