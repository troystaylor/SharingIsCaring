import { getConfig } from "../config/connectorConfig";

interface TokenResponse {
  access_token: string;
  instance_url: string;
  id: string;
  token_type: string;
  issued_at: string;
  signature: string;
}

interface CachedToken {
  accessToken: string;
  instanceUrl: string;
  issuedAt: number;
}

const TOKEN_REFRESH_BUFFER_MS = 30 * 60 * 1000; // Refresh 30 min before assumed expiry
let cachedToken: CachedToken | null = null;

// ── Client Credentials Flow ──
// Server-to-server: exchanges client_id + client_secret for an access token.
// Requires a Connected App with Client Credentials enabled and an assigned integration user.

async function authenticateClientCredentials(): Promise<CachedToken> {
  const config = getConfig().salesforce;

  const params = new URLSearchParams({
    grant_type: "client_credentials",
    client_id: config.clientId,
    client_secret: config.clientSecret,
  });

  const response = await fetch(
    `${config.instanceUrl}/services/oauth2/token`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
      },
      body: params.toString(),
    }
  );

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `Salesforce client_credentials auth failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as TokenResponse;

  cachedToken = {
    accessToken: data.access_token,
    instanceUrl: data.instance_url,
    issuedAt: parseInt(data.issued_at, 10),
  };

  console.log(
    `[Auth] Client credentials token obtained. Instance: ${cachedToken.instanceUrl}`
  );
  return cachedToken;
}

// ── Refresh Token Flow ──
// Uses a stored refresh_token to obtain a new access token (original flow).

async function authenticateRefreshToken(): Promise<CachedToken> {
  const config = getConfig().salesforce;

  const params = new URLSearchParams({
    grant_type: "refresh_token",
    client_id: config.clientId,
    client_secret: config.clientSecret,
    refresh_token: config.refreshToken,
  });

  const response = await fetch(
    `${config.instanceUrl}/services/oauth2/token`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
      },
      body: params.toString(),
    }
  );

  if (!response.ok) {
    const errorBody = await response.text();
    throw new Error(
      `Salesforce token refresh failed (${response.status}): ${errorBody}`
    );
  }

  const data = (await response.json()) as TokenResponse;

  cachedToken = {
    accessToken: data.access_token,
    instanceUrl: data.instance_url,
    issuedAt: parseInt(data.issued_at, 10),
  };

  console.log(
    `[Auth] Access token refreshed. Instance: ${cachedToken.instanceUrl}`
  );
  return cachedToken;
}

// ── Public API ──

export async function refreshAccessToken(): Promise<CachedToken> {
  const config = getConfig().salesforce;

  if (config.authFlow === "client_credentials") {
    return authenticateClientCredentials();
  }
  return authenticateRefreshToken();
}

function isTokenExpired(): boolean {
  if (!cachedToken) return true;
  const elapsed = Date.now() - cachedToken.issuedAt;
  return elapsed >= TOKEN_REFRESH_BUFFER_MS;
}

export async function getAccessToken(): Promise<string> {
  if (isTokenExpired()) {
    await refreshAccessToken();
  }
  return cachedToken!.accessToken;
}

export async function getInstanceUrl(): Promise<string> {
  if (!cachedToken) {
    await refreshAccessToken();
  }
  return cachedToken!.instanceUrl;
}
