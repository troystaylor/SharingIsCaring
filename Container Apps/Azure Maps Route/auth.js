'use strict';

const crypto = require('crypto');

/**
 * Validates Microsoft Entra v2 access tokens with no dependencies: RS256 signature against the tenant's published
 * keys, issuer, audience, expiry and the required delegated scope.
 */
function createEntraValidator({ tenantId, clientId, requiredScope = 'access_as_user', fetchImpl = fetch, now = () => Date.now() }) {
  const issuer = `https://login.microsoftonline.com/${tenantId}/v2.0`;
  const jwksUrl = `https://login.microsoftonline.com/${tenantId}/discovery/v2.0/keys`;
  let keys = new Map();
  let keysLoadedAt = 0;

  async function loadKeys(force) {
    const stale = now() - keysLoadedAt > 6 * 60 * 60 * 1000;
    if (!force && !stale && keys.size) return;
    const res = await fetchImpl(jwksUrl);
    if (!res.ok) throw new Error('Could not load signing keys');
    const { keys: list } = await res.json();
    keys = new Map(list.filter((k) => k.kty === 'RSA').map((k) => [k.kid, crypto.createPublicKey({ key: k, format: 'jwk' })]));
    keysLoadedAt = now();
  }

  const b64 = (s) => Buffer.from(s, 'base64url');

  async function validate(authorization) {
    const match = /^Bearer\s+(.+)$/i.exec(authorization || '');
    if (!match) return { ok: false, reason: 'missing_token' };

    const parts = match[1].trim().split('.');
    if (parts.length !== 3) return { ok: false, reason: 'malformed_token' };

    let header, claims;
    try {
      header = JSON.parse(b64(parts[0]).toString());
      claims = JSON.parse(b64(parts[1]).toString());
    } catch {
      return { ok: false, reason: 'malformed_token' };
    }

    if (header.alg !== 'RS256') return { ok: false, reason: 'unsupported_algorithm' };

    try {
      await loadKeys(false);
      if (!keys.has(header.kid)) await loadKeys(true);
    } catch {
      return { ok: false, reason: 'keys_unavailable' };
    }

    const key = keys.get(header.kid);
    if (!key) return { ok: false, reason: 'unknown_signing_key' };

    const valid = crypto.verify('RSA-SHA256', Buffer.from(`${parts[0]}.${parts[1]}`), key, b64(parts[2]));
    if (!valid) return { ok: false, reason: 'bad_signature' };

    const seconds = Math.floor(now() / 1000);
    if (claims.iss !== issuer) return { ok: false, reason: 'wrong_issuer' };
    if (claims.aud !== clientId) return { ok: false, reason: 'wrong_audience' };
    if (typeof claims.exp !== 'number' || claims.exp + 60 < seconds) return { ok: false, reason: 'expired' };
    if (typeof claims.nbf === 'number' && claims.nbf - 60 > seconds) return { ok: false, reason: 'not_yet_valid' };
    if (claims.tid !== tenantId) return { ok: false, reason: 'wrong_tenant' };
    if (!String(claims.scp || '').split(' ').includes(requiredScope)) return { ok: false, reason: 'missing_scope' };

    return { ok: true, subject: claims.oid || claims.sub, name: claims.preferred_username };
  }

  return { validate };
}

module.exports = { createEntraValidator };
