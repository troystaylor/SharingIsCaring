"use strict";
// ── ServiceNow OAuth Authentication ──
// Supports client_credentials and password grant flows.
Object.defineProperty(exports, "__esModule", { value: true });
exports.refreshAccessToken = refreshAccessToken;
exports.getAccessToken = getAccessToken;
const connectorConfig_1 = require("../config/connectorConfig");
const TOKEN_REFRESH_BUFFER_MS = 60 * 1000; // Refresh 60s before expiry
let cachedToken = null;
async function authenticateClientCredentials() {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    const params = new URLSearchParams({
        grant_type: "client_credentials",
        client_id: config.clientId,
        client_secret: config.clientSecret,
    });
    const response = await fetch(`${config.instanceUrl}/oauth_token.do`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: params.toString(),
    });
    if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(`ServiceNow client_credentials auth failed (${response.status}): ${errorBody}`);
    }
    const data = (await response.json());
    cachedToken = {
        accessToken: data.access_token,
        expiresAt: Date.now() + data.expires_in * 1000,
    };
    console.log("[Auth] Client credentials token obtained");
    return cachedToken;
}
async function authenticatePassword() {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    const params = new URLSearchParams({
        grant_type: "password",
        client_id: config.clientId,
        client_secret: config.clientSecret,
        username: config.username,
        password: config.password,
    });
    const response = await fetch(`${config.instanceUrl}/oauth_token.do`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: params.toString(),
    });
    if (!response.ok) {
        const errorBody = await response.text();
        throw new Error(`ServiceNow password auth failed (${response.status}): ${errorBody}`);
    }
    const data = (await response.json());
    cachedToken = {
        accessToken: data.access_token,
        expiresAt: Date.now() + data.expires_in * 1000,
    };
    console.log("[Auth] Password grant token obtained");
    return cachedToken;
}
function isTokenExpired() {
    if (!cachedToken)
        return true;
    return Date.now() >= cachedToken.expiresAt - TOKEN_REFRESH_BUFFER_MS;
}
async function refreshAccessToken() {
    const config = (0, connectorConfig_1.getConfig)().servicenow;
    if (config.authFlow === "client_credentials") {
        return authenticateClientCredentials();
    }
    return authenticatePassword();
}
async function getAccessToken() {
    if (isTokenExpired()) {
        await refreshAccessToken();
    }
    return cachedToken.accessToken;
}
//# sourceMappingURL=servicenowAuth.js.map