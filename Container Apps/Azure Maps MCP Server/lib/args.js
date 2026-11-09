'use strict';

const { VERSIONS } = require('./maps');

/** Thrown for problems with the caller's input, so the message can go back to the agent as is. */
class InputError extends Error {
  constructor(message) {
    super(message);
    this.name = 'InputError';
  }
}

const isBlank = (v) =>
  v === null || v === undefined ||
  (typeof v === 'string' && v.trim() === '') ||
  (Array.isArray(v) && v.length === 0);

// Copilot sends every optional input, using 0 for numbers the user left out. For these names zero is never meaningful.
const ZERO_MEANS_BLANK = new Set([
  'maxRouteCount', 'routeWidth', 'width', 'height', 'timeBudgetInSec', 'distanceBudgetInMeters', 'top', 'limit', 'radius',
  'zoom', 'maxAlternatives', 'duration', 'interval', 'maxDetourTime', 'ofs', 'offset', 'year', 'govId', 'heading',
  'minDeviationDistance', 'minDeviationTime', 'vehicleWeight', 'vehicleWidth', 'vehicleHeight', 'vehicleLength',
  'vehicleAxleWeight', 'vehicleMaxSpeed', 'budgetMinutes', 'steps', 'indexId', 'indexGroupId', 'transitionsYears'
]);

/** Drops the inputs the agent left blank, including inside nested objects and lists. */
function removeBlankArguments(args) {
  const clean = (value, key) => {
    if (Array.isArray(value)) {
      const items = value.map((v) => clean(v)).filter((v) => !isBlank(v));
      return items;
    }
    if (value && typeof value === 'object') {
      const out = {};
      for (const [k, v] of Object.entries(value)) {
        const c = clean(v, k);
        if (isBlank(c)) continue;
        out[k] = c;
      }
      // An unused place arrives as longitude 0 and latitude 0 with no text; that is never a real request.
      if (out.longitude === 0 && out.latitude === 0 && !out.query && !out.name && !out.address) return undefined;
      return Object.keys(out).length ? out : undefined;
    }
    if (typeof value === 'number' && key && ZERO_MEANS_BLANK.has(key) && value === 0) return undefined;
    return value;
  };
  const out = {};
  for (const [k, v] of Object.entries(args || {})) {
    const c = clean(v, k);
    if (!isBlank(c)) out[k] = c;
  }
  return out;
}

function number(value, name) {
  const n = typeof value === 'string' ? Number(value.trim()) : value;
  if (typeof n !== 'number' || !Number.isFinite(n)) throw new InputError(`${name} must be a number.`);
  return n;
}

function requireNumber(source, name) {
  const v = source[name];
  if (v === undefined || v === null || v === '') throw new InputError(`Missing required argument: ${name}`);
  return number(v, name);
}

function intInRange(value, name, min, max, fallback) {
  if (value === undefined || value === null) return fallback;
  const n = Math.round(number(value, name));
  if (n < min || n > max) throw new InputError(`${name} must be between ${min} and ${max}.`);
  return n;
}

function oneOf(value, name, allowed, fallback) {
  if (value === undefined || value === null || value === '') return fallback;
  const found = allowed.find((a) => String(a).toLowerCase() === String(value).toLowerCase());
  if (found === undefined) throw new InputError(`${name} must be one of: ${allowed.join(', ')}.`);
  return found;
}

/** A list given as an array or as comma-separated text. */
function list(value) {
  if (value === undefined || value === null) return [];
  if (Array.isArray(value)) return value.map((v) => String(v).trim()).filter(Boolean);
  return String(value).split(',').map((s) => s.trim()).filter(Boolean);
}

function assertLonLat(lon, lat, label = 'location') {
  if (lon < -180 || lon > 180) throw new InputError(`The longitude of ${label} must be between -180 and 180.`);
  if (lat < -90 || lat > 90) throw new InputError(`The latitude of ${label} must be between -90 and 90.`);
}

/**
 * Reads a location from what the agent sent: an object with longitude and latitude, an object with query text,
 * a [longitude, latitude] pair, or plain text. Returns { longitude, latitude } or { query }, plus an optional name.
 */
function parsePlace(value, label = 'location') {
  if (value === undefined || value === null || value === '') throw new InputError(`Missing required location: ${label}`);

  if (typeof value === 'string') return { query: value.trim() };

  if (Array.isArray(value)) {
    if (value.length >= 2 && value.every((v) => typeof v === 'number' || (typeof v === 'string' && v.trim() !== ''))) {
      const longitude = number(value[0], label), latitude = number(value[1], label);
      assertLonLat(longitude, latitude, label);
      return { longitude, latitude };
    }
    throw new InputError(`${label} must be an object with query or longitude and latitude.`);
  }

  if (typeof value === 'object') {
    const lon = value.longitude ?? value.lon ?? value.lng;
    const lat = value.latitude ?? value.lat;
    const name = value.name ? String(value.name) : undefined;
    const text = value.query ?? value.address ?? value.text;
    // Agents often fill unused numbers with 0, so 0,0 next to a query means the query is what was meant.
    const placeholderZeros = Number(lon) === 0 && Number(lat) === 0 && !isBlank(text);
    if (!isBlank(lon) && !isBlank(lat) && !placeholderZeros) {
      const longitude = number(lon, `${label} longitude`), latitude = number(lat, `${label} latitude`);
      assertLonLat(longitude, latitude, label);
      return { longitude, latitude, ...(name ? { name } : {}) };
    }
    if (!isBlank(text)) return { query: String(text).trim(), ...(name ? { name } : {}) };
  }
  throw new InputError(`${label} needs a query (text) or both longitude and latitude.`);
}

/**
 * Turns locations into coordinates, looking up text with the Azure Maps geocoder. Lookups are cached for the life of
 * one tool call, so repeating a place doesn't cost another request.
 */
function createPlaceResolver(client) {
  const cache = new Map();

  async function lookup(query, near) {
    const key = query.toLowerCase();
    if (!cache.has(key)) {
      cache.set(key, (async () => {
        const data = await client.json('/geocode', {
          version: VERSIONS.search,
          accept: 'application/geo+json',
          query: { query, top: 1, coordinates: near ? [near.longitude, near.latitude] : undefined }
        });
        const feature = (data.features || [])[0];
        if (!feature || !feature.geometry || !Array.isArray(feature.geometry.coordinates)) {
          throw new InputError(`No location was found for "${query}". Try a more specific address or give coordinates.`);
        }
        const a = (feature.properties && feature.properties.address) || {};
        return {
          longitude: feature.geometry.coordinates[0],
          latitude: feature.geometry.coordinates[1],
          name: a.formattedAddress || query,
          bbox: feature.bbox
        };
      })());
      cache.get(key).catch(() => cache.delete(key));
    }
    return cache.get(key);
  }

  /** Resolves one location. Returns { longitude, latitude, name, query? }. */
  async function resolve(value, label = 'location', near) {
    const parsed = parsePlace(value, label);
    if (parsed.query) {
      const found = await lookup(parsed.query, near);
      return { ...found, name: parsed.name || found.name, query: parsed.query };
    }
    return { longitude: parsed.longitude, latitude: parsed.latitude, name: parsed.name };
  }

  async function resolveAll(values, label = 'location') {
    if (!Array.isArray(values) || values.length === 0) throw new InputError(`Missing required list: ${label}`);
    return Promise.all(values.map((v, i) => resolve(v, `${label} ${i + 1}`)));
  }

  const addressCache = new Map();

  /**
   * Gives coordinate-only points a readable name by reverse geocoding them, so tables and legends can say where a point
   * is instead of "Point 3". Only the first `max` unnamed points are looked up, to keep large lists cheap.
   */
  async function nameAll(points, max = 25) {
    let budget = max;
    await Promise.all(points.map(async (p) => {
      if (p.name || p.query || budget <= 0) return;
      budget--;
      const key = `${p.longitude.toFixed(4)},${p.latitude.toFixed(4)}`;
      if (!addressCache.has(key)) {
        addressCache.set(key, client.json('/reverseGeocode', {
          version: VERSIONS.search, accept: 'application/geo+json', query: { coordinates: [p.longitude, p.latitude] }
        }).then((data) => {
          const a = (((data.features || [])[0] || {}).properties || {}).address || {};
          return a.addressLine ? [a.addressLine, a.locality].filter(Boolean).join(', ') : a.formattedAddress || '';
        }).catch(() => ''));
      }
      const name = await addressCache.get(key);
      if (name) p.name = name;
    }));
    return points;
  }

  return { resolve, resolveAll, nameAll };
}

const labelOf = (p, fallback) => p.name || (p.query ? p.query : fallback) || `${p.latitude.toFixed(4)}, ${p.longitude.toFixed(4)}`;

module.exports = {
  InputError, removeBlankArguments, number, requireNumber, intInRange, oneOf, list, assertLonLat,
  parsePlace, createPlaceResolver, labelOf, isBlank
};
