'use strict';

/**
 * Supplies the headers that authenticate this server to Azure Maps.
 *
 * In Azure Container Apps the managed identity is used (Entra auth with x-ms-client-id), so no key exists anywhere.
 * For local development only, set MAPS_SUBSCRIPTION_KEY to use a shared key instead.
 */
function createMapsAuth({ mapsClientId, env = process.env, fetchImpl = fetch, now = () => Date.now() }) {
  let cached = null;

  async function managedIdentityToken() {
    if (cached && cached.expiresAt - now() > 5 * 60 * 1000) return cached.token;

    const url = `${env.IDENTITY_ENDPOINT}?resource=${encodeURIComponent('https://atlas.microsoft.com/')}&api-version=2019-08-01`;
    const res = await fetchImpl(url, { headers: { 'X-IDENTITY-HEADER': env.IDENTITY_HEADER } });
    if (!res.ok) throw new Error('Could not get a managed identity token for Azure Maps');

    const body = await res.json();
    const expiresAt = body.expires_on ? Number(body.expires_on) * 1000 : now() + 50 * 60 * 1000;
    cached = { token: body.access_token, expiresAt };
    return cached.token;
  }

  async function headers() {
    if (env.IDENTITY_ENDPOINT && env.IDENTITY_HEADER) {
      if (!mapsClientId) throw new Error('MAPS_CLIENT_ID is not configured');
      return { Authorization: `Bearer ${await managedIdentityToken()}`, 'x-ms-client-id': mapsClientId };
    }
    if (env.MAPS_SUBSCRIPTION_KEY) return { 'subscription-key': env.MAPS_SUBSCRIPTION_KEY };
    throw new Error('No Azure Maps credential is configured on the server');
  }

  return { headers };
}

module.exports = { createMapsAuth };
