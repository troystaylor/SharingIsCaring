'use strict';

const MAPS_HOST = 'https://atlas.microsoft.com';

const VERSIONS = {
  search: '2026-01-01',
  searchV1: '1.0',
  route: '2025-01-01',
  routeV1: '1.0',
  render: '2024-04-01',
  weather: '1.1',
  timezone: '1.0',
  traffic: '2025-01-01',
  trafficV1: '1.0',
  geolocation: '1.0'
};

const RETRY_STATUSES = new Set([429, 502, 503, 504]);
const MAX_ATTEMPTS = 3;
const TIMEOUT_MS = 30000;

/**
 * Limits how many requests are in flight to Azure Maps at once, across all callers. A widget can ask for dozens of
 * tiles at the same time, and bursts of new connections are slow and can time out.
 */
const MAX_CONCURRENT = Number(process.env.MAPS_MAX_CONCURRENT) || 16;
let active = 0;
const waiting = [];
function acquire() {
  if (active < MAX_CONCURRENT) { active++; return Promise.resolve(release); }
  return new Promise((resolve) => waiting.push(resolve));
}
function release() {
  const next = waiting.shift();
  if (next) next(release); else active--;
}

class MapsError extends Error {
  constructor(message, status) {
    super(message);
    this.name = 'MapsError';
    this.status = status;
  }
}

/** Azure Maps error bodies are JSON with error.message (or a plain string). Keep the short readable part. */
function readableError(status, statusText, body) {
  let detail = String(body || '').trim();
  try {
    const parsed = JSON.parse(detail);
    const message = (parsed.error && (parsed.error.message || parsed.error.code)) || parsed.message || parsed.detailedError?.message;
    if (message) detail = String(message);
  } catch { /* not JSON */ }
  detail = detail.replace(/subscription-key=[^&\s"]+/gi, 'subscription-key=***');
  if (detail.length > 400) detail = detail.slice(0, 400) + '...';
  return `Azure Maps returned ${status} ${statusText}${detail ? ': ' + detail : ''}`;
}

// The static map API reads these as repeated parameters, one per pin or path.
const REPEATED = new Set(['pins', 'path']);

/** Builds a query string. Arrays are comma-joined (or repeated for pins and path), and blank values are skipped. */
function toQuery(params) {
  const parts = [];
  for (const [key, value] of Object.entries(params || {})) {
    if (value === undefined || value === null || value === '') continue;
    if (Array.isArray(value) && REPEATED.has(key)) {
      for (const v of value) parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(v))}`);
      continue;
    }
    const text = Array.isArray(value) ? value.join(',') : String(value);
    if (text === '') continue;
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(text)}`);
  }
  return parts.join('&');
}

/**
 * A small Azure Maps REST client. Authentication comes from the maps-auth module (managed identity in Azure), so no key
 * is passed around. Transient failures (429 and 5xx) are retried with backoff, and every call has a timeout.
 */
function createMapsClient(auth, { fetchImpl = fetch, sleep = (ms) => new Promise((r) => setTimeout(r, ms)) } = {}) {
  async function call(path, options = {}) {
    const release = await acquire();
    try {
      return await callOnce(path, options);
    } finally {
      release();
    }
  }

  async function callOnce(path, options = {}) {
    const { method = 'GET', query, version, body, contentType = 'application/json', accept, language, headers: extra } = options;
    const q = toQuery({ 'api-version': version, ...(query || {}) });
    const url = `${MAPS_HOST}${path}${q ? '?' + q : ''}`;

    let lastError;
    for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
      try {
        const headers = { ...(await auth.headers()), ...(extra || {}) };
        if (accept) headers.Accept = accept;
        if (language) headers['Accept-Language'] = language;
        if (body !== undefined) headers['Content-Type'] = contentType;

        const response = await fetchImpl(url, {
          method,
          headers,
          body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body),
          signal: AbortSignal.timeout(TIMEOUT_MS)
        });

        if (RETRY_STATUSES.has(response.status) && attempt < MAX_ATTEMPTS - 1) {
          const retryAfter = Number(response.headers.get('retry-after'));
          await sleep(Math.min(Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 400 * 2 ** attempt, 5000) + Math.random() * 150);
          continue;
        }
        if (!response.ok && response.status !== 202) {
          throw new MapsError(readableError(response.status, response.statusText, await response.text()), response.status);
        }
        return response;
      } catch (err) {
        if (err instanceof MapsError) throw err;
        lastError = err;
        if (attempt < MAX_ATTEMPTS - 1) {
          await sleep(400 * 2 ** attempt + Math.random() * 150);
          continue;
        }
      }
    }
    const cause = lastError && lastError.cause && (lastError.cause.code || lastError.cause.message);
    throw new MapsError(`Azure Maps could not be reached: ${lastError && lastError.message}${cause ? ` (${cause})` : ''}`, 0);
  }

  /** Returns parsed JSON. For accepted (202) responses it returns { accepted: true, location }. */
  async function json(path, options = {}) {
    const response = await call(path, { accept: 'application/json, application/geo+json;q=0.9', ...options });
    if (response.status === 202) {
      return { accepted: true, location: response.headers.get('operation-location') || response.headers.get('location') };
    }
    const text = await response.text();
    if (!text) return {};
    try {
      return JSON.parse(text);
    } catch {
      throw new MapsError('Azure Maps returned a response that is not JSON.', response.status);
    }
  }

  /** Returns { bytes, mimeType } for images and other binary responses. */
  async function binary(path, options = {}) {
    const response = await call(path, options);
    const bytes = Buffer.from(await response.arrayBuffer());
    const mimeType = (response.headers.get('content-type') || 'application/octet-stream').split(';')[0].trim();
    return { bytes, mimeType };
  }

  /**
   * Runs many single requests and returns them in the batch API's shape, { batchItems: [...] }. The Azure Maps batch
   * endpoints need a data-write role, which the server's identity deliberately doesn't have, so batches are sent as
   * individual requests. The client's concurrency cap keeps this gentle. A failed item becomes { error: { message } }.
   */
  async function batch(items, fn) {
    const batchItems = await Promise.all(items.map(async (item, i) => {
      try {
        return await fn(item, i);
      } catch (err) {
        return { error: { message: err.message } };
      }
    }));
    return { batchItems };
  }

  return { call, json, binary, batch };
}

module.exports = { createMapsClient, MapsError, VERSIONS, toQuery, MAPS_HOST };
