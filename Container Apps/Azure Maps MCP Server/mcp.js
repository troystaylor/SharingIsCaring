'use strict';

const { TOOLS, get } = require('./lib/registry');
const { resourceList, resourceContent, RESOURCES } = require('./lib/widgets');
const { createMapsClient } = require('./lib/maps');
const { createPlaceResolver, removeBlankArguments, InputError } = require('./lib/args');

const SERVER_NAME = 'azure-maps-mcp';
const SERVER_VERSION = '1.0.0';
const SUPPORTED_PROTOCOLS = ['2025-06-18', '2025-03-26', '2024-11-05'];

const INSTRUCTIONS = [
  'Azure Maps for search, routing, weather, traffic, time zones, maps and geolocation.',
  'Most tools accept a place as text (an address or name) or as longitude and latitude, so you rarely need to geocode first.',
  'Several tools show an interactive app (map, route planner, weather dashboard, traffic, reachable area, travel time matrix, departure time chart, world clock, batch geocoding, storm tracker) when the client supports it.',
  'If the user cannot see an app, summarize the result in text and never draw a map yourself.',
  'Coordinates are longitude then latitude in GeoJSON and in the longitude and latitude fields of tools.'
].join(' ');

const rpcResult = (id, result) => ({ status: 200, body: { jsonrpc: '2.0', result, id } });
const rpcError = (id, code, message, data) => ({
  status: 200,
  body: { jsonrpc: '2.0', error: { code, message, ...(data ? { data } : {}) }, id }
});

const wwwAuthenticate = (reason) => `Bearer realm="${SERVER_NAME}"${reason ? `, error="invalid_token", error_description="${reason}"` : ''}`;

/** A fixed-window limit per signed-in user, so one caller cannot use up the Azure Maps quota. */
function createRateLimiter({ limit = 600, windowMs = 60 * 1000, now = () => Date.now() } = {}) {
  const buckets = new Map();
  return (key) => {
    const t = now();
    let b = buckets.get(key);
    if (!b || t - b.start >= windowMs) {
      b = { start: t, count: 0 };
      buckets.set(key, b);
      if (buckets.size > 5000) for (const [k, v] of buckets) if (t - v.start >= windowMs) buckets.delete(k);
    }
    b.count++;
    return b.count <= limit;
  };
}

async function callTool(name, rawArgs, deps) {
  const tool = get(name);
  if (!tool) return null;
  const client = deps.client || createMapsClient(deps.maps);
  const ctx = { maps: client };
  ctx.places = createPlaceResolver(client);
  return tool.handler(removeBlankArguments(rawArgs), ctx);
}

/**
 * Handles one JSON-RPC message. Discovery (initialize, tools/list, resources) is open so clients can read the tool
 * descriptions and the apps. tools/call needs a valid Microsoft Entra access token, and Azure Maps is reached with the
 * server's own identity, so callers never handle an Azure Maps key.
 */
async function handleMcp(message, authorization, deps) {
  if (!message || Array.isArray(message) || message.jsonrpc !== '2.0' || typeof message.method !== 'string') {
    return rpcError(message && message.id !== undefined ? message.id : null, -32600, 'Invalid Request');
  }

  const { method, id, params = {} } = message;
  const isNotification = id === undefined;

  switch (method) {
    case 'initialize': {
      const requested = params.protocolVersion;
      return rpcResult(id, {
        protocolVersion: SUPPORTED_PROTOCOLS.includes(requested) ? requested : SUPPORTED_PROTOCOLS[1],
        capabilities: { tools: { listChanged: false }, resources: { listChanged: false } },
        serverInfo: { name: SERVER_NAME, title: 'Azure Maps', version: SERVER_VERSION },
        instructions: INSTRUCTIONS
      });
    }
    case 'notifications/initialized':
    case 'notifications/cancelled':
      return { status: 202 };
    case 'ping':
      return rpcResult(id, {});
    case 'tools/list':
      return rpcResult(id, { tools: TOOLS });
    case 'resources/list':
      return rpcResult(id, { resources: resourceList() });
    case 'resources/read': {
      if (!RESOURCES[params.uri]) return rpcError(id, -32002, 'Resource not found', params.uri);
      return rpcResult(id, { contents: [resourceContent(params.uri)] });
    }
    case 'tools/call': {
      const auth = await deps.authenticate(authorization);
      if (!auth.ok) {
        return {
          status: 401,
          headers: { 'WWW-Authenticate': wwwAuthenticate(auth.reason === 'missing_token' ? null : auth.reason) },
          body: { jsonrpc: '2.0', error: { code: -32001, message: 'Sign in with Microsoft Entra to use this server.' }, id: id ?? null }
        };
      }
      if (deps.limiter && !deps.limiter(auth.subject || 'anonymous')) {
        return rpcResult(id, { content: [{ type: 'text', text: 'Too many requests. Wait a moment and try again.' }], isError: true });
      }
      try {
        const result = await callTool(params.name, params.arguments || {}, deps);
        if (!result) return rpcError(id, -32602, 'Unknown tool', params.name);
        return rpcResult(id, result);
      } catch (err) {
        const text = err instanceof InputError ? err.message : err.name === 'MapsError' ? err.message : `The tool failed: ${err.message}`;
        if (!(err instanceof InputError) && err.name !== 'MapsError') console.error(`Tool ${params.name} failed`, err);
        return rpcResult(id, { content: [{ type: 'text', text }], isError: true });
      }
    }
    default:
      if (isNotification) return { status: 202 };
      return rpcError(id, -32601, 'Method not found', method);
  }
}

module.exports = { handleMcp, createRateLimiter, callTool, SERVER_NAME };
