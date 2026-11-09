'use strict';

// Offline tests: no network and no Azure Maps key needed.   npm test
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const { handleMcp, callTool, createRateLimiter } = require('../mcp');
const { TOOLS } = require('../lib/registry');
const { RESOURCES } = require('../lib/widgets');
const { createMapsClient, toQuery, MapsError } = require('../lib/maps');
const { removeBlankArguments, parsePlace, InputError } = require('../lib/args');
const geo = require('../lib/geo');

let passed = 0;
let failed = 0;
async function test(name, fn) {
  try {
    await fn();
    passed++;
    console.log(`PASS  ${name}`);
  } catch (err) {
    failed++;
    console.log(`FAIL  ${name}\n      ${err.stack.split('\n').slice(0, 3).join('\n      ')}`);
  }
}

const rpc = (method, params, id = 1) => ({ jsonrpc: '2.0', id, method, params });
const authOk = async () => ({ ok: true, subject: 'tester' });

(async () => {
  // ───────────── registry and apps ─────────────

  await test('every tool has a name, description and object schema', () => {
    const names = new Set();
    for (const t of TOOLS) {
      assert(/^[a-z][a-z0-9_]*$/.test(t.name), `bad name ${t.name}`);
      assert(!names.has(t.name), `duplicate ${t.name}`);
      names.add(t.name);
      assert(t.description && t.description.length > 20, `${t.name} needs a description`);
      assert.strictEqual(t.inputSchema.type, 'object');
      for (const r of t.inputSchema.required || []) assert(t.inputSchema.properties[r], `${t.name} requires unknown ${r}`);
      assert.strictEqual(t.annotations.readOnlyHint, true);
    }
    assert(TOOLS.length >= 55, `expected the full API surface, got ${TOOLS.length}`);
  });

  await test('EXAMPLES.md has a valid example for every tool', () => {
    const md = fs.readFileSync(path.join(__dirname, '..', 'EXAMPLES.md'), 'utf8');
    const sections = md.split(/\r?\n### `/).slice(1).map((s) => ({ name: s.slice(0, s.indexOf('`')), body: s }));
    const documented = new Set(sections.map((s) => s.name));
    for (const t of TOOLS) assert(documented.has(t.name), `EXAMPLES.md is missing ${t.name}`);
    for (const s of sections) {
      const tool = TOOLS.find((t) => t.name === s.name);
      assert(tool, `EXAMPLES.md documents unknown tool ${s.name}`);
      const block = /```json\r?\n([\s\S]*?)```/.exec(s.body);
      assert(block, `${s.name} has no JSON example`);
      const args = JSON.parse(block[1]);
      for (const key of Object.keys(args)) assert(tool.inputSchema.properties[key], `${s.name} example uses unknown argument ${key}`);
      for (const r of tool.inputSchema.required || []) assert(r in args, `${s.name} example is missing required ${r}`);
    }
  });

  await test('every widget a tool declares exists, and every widget is used', () => {
    const used = new Set(TOOLS.map((t) => t._meta && t._meta.ui && t._meta.ui.resourceUri).filter(Boolean));
    for (const uri of used) assert(RESOURCES[uri], `missing page for ${uri}`);
    for (const uri of Object.keys(RESOURCES)) assert(used.has(uri), `${uri} is not used by any tool`);
  });

  await test('widgets load nothing from the network', () => {
    for (const [uri, w] of Object.entries(RESOURCES)) {
      assert(!/<script[^>]+src=/i.test(w.html), `${uri} loads a script`);
      assert(!/<link[^>]+href=/i.test(w.html), `${uri} loads a stylesheet`);
      assert(!/<img[^>]+src=["']https?:/i.test(w.html), `${uri} loads a remote image`);
      assert(!/\bfetch\(|XMLHttpRequest|WebSocket|EventSource/.test(w.html), `${uri} makes network calls`);
      assert(!/@shared-(css|js)/.test(w.html.replace(/\/\*@shared-(css|js)\*\//g, '')), `${uri} has an unreplaced placeholder`);
      assert(w.html.includes('App.init('), `${uri} never starts the app bridge`);
    }
  });

  // ───────────── protocol ─────────────

  await test('discovery is open and tools/call needs a token', async () => {
    const deps = { authenticate: async () => ({ ok: false, reason: 'missing_token' }), maps: {} };
    let r = await handleMcp(rpc('initialize', { protocolVersion: '2025-06-18' }), undefined, deps);
    assert.strictEqual(r.body.result.serverInfo.name, 'azure-maps-mcp');
    assert.strictEqual(r.body.result.protocolVersion, '2025-06-18');
    r = await handleMcp(rpc('tools/list'), undefined, deps);
    assert.strictEqual(r.body.result.tools.length, TOOLS.length);
    assert(!r.body.result.tools.some((t) => 'handler' in t), 'handlers must not be listed');
    r = await handleMcp(rpc('resources/list'), undefined, deps);
    assert(r.body.result.resources.every((x) => x.mimeType === 'text/html;profile=mcp-app'));
    r = await handleMcp(rpc('resources/read', { uri: 'ui://azure-maps/map.html' }), undefined, deps);
    assert(r.body.result.contents[0].text.includes('<canvas') || r.body.result.contents[0].text.includes('MapView'));
    r = await handleMcp(rpc('tools/call', { name: 'geocode', arguments: { query: 'x' } }), undefined, deps);
    assert.strictEqual(r.status, 401);
    assert(/Bearer/.test(r.headers['WWW-Authenticate']));
  });

  await test('notifications return 202 and unknown methods are errors', async () => {
    const deps = { authenticate: authOk, maps: {} };
    assert.strictEqual((await handleMcp({ jsonrpc: '2.0', method: 'notifications/initialized' }, undefined, deps)).status, 202);
    const r = await handleMcp(rpc('nope'), undefined, deps);
    assert.strictEqual(r.body.error.code, -32601);
  });

  await test('the rate limiter stops a caller who goes over', async () => {
    const limiter = createRateLimiter({ limit: 2 });
    const deps = { authenticate: authOk, maps: {}, limiter, client: { json: async () => ({ features: [] }) } };
    const call = () => handleMcp(rpc('tools/call', { name: 'get_ip_location', arguments: { ip: '8.8.8.8' } }), 'Bearer x', deps);
    await call(); await call();
    const third = await call();
    assert.strictEqual(third.body.result.isError, true);
    assert(/Too many/.test(third.body.result.content[0].text));
  });

  // ───────────── helpers ─────────────

  await test('blank arguments are removed, including zero placeholders', () => {
    const out = removeBlankArguments({ a: '', b: [], c: { query: '', longitude: 0, latitude: 0 }, top: 0, ok: 0, name: 'x', near: { query: 'Seattle', longitude: 0, latitude: 0 } });
    assert.deepStrictEqual(out, { ok: 0, name: 'x', near: { query: 'Seattle', longitude: 0, latitude: 0 } });
  });

  await test('places can be text, coordinates, a pair, or an object; zeros next to a query mean the query', () => {
    assert.deepStrictEqual(parsePlace('Seattle'), { query: 'Seattle' });
    assert.deepStrictEqual(parsePlace([-122.3, 47.6]), { longitude: -122.3, latitude: 47.6 });
    assert.deepStrictEqual(parsePlace({ longitude: '-122.3', latitude: '47.6', name: 'HQ' }), { longitude: -122.3, latitude: 47.6, name: 'HQ' });
    assert.deepStrictEqual(parsePlace({ query: 'Seattle', longitude: 0, latitude: 0 }), { query: 'Seattle' });
    assert.throws(() => parsePlace({ longitude: 200, latitude: 0 }), InputError);
    assert.throws(() => parsePlace({}), InputError);
  });

  await test('geometry helpers', () => {
    const line = Array.from({ length: 200 }, (_, i) => [i / 100, Math.sin(i / 10) / 100]);
    const simple = geo.simplifyLine(line, 20);
    assert.strictEqual(simple.length, 20);
    assert.deepStrictEqual(simple[0], line[0]);
    assert.deepStrictEqual(simple[19], line[199]);
    const square = { type: 'Polygon', coordinates: [[[0, 0], [0, 0.1], [0.1, 0.1], [0.1, 0], [0, 0]]] };
    const area = geo.geometryAreaSqm(square) / 1e6;
    assert(area > 120 && area < 125, `area ${area}`);
    assert.strictEqual(geo.formatDuration(3700), '1 h 2 min');
    assert.strictEqual(geo.formatDistance(1609.344, 'imperial'), '1.0 mi');
  });

  // ───────────── Azure Maps client ─────────────

  await test('the client builds queries, repeats pins and paths, and retries transient failures', async () => {
    assert.strictEqual(toQuery({ a: 1, b: [1, 2], c: '', d: undefined, pins: ['x|y', 'z'] }), 'a=1&b=1%2C2&pins=x%7Cy&pins=z');
    let calls = 0;
    const fetchImpl = async () => {
      calls++;
      if (calls < 3) return new Response('busy', { status: 503, statusText: 'Service Unavailable', headers: { 'retry-after': '0' } });
      return new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'content-type': 'application/json' } });
    };
    const client = createMapsClient({ headers: async () => ({}) }, { fetchImpl, sleep: async () => {} });
    assert.deepStrictEqual(await client.json('/x', { version: '1' }), { ok: true });
    assert.strictEqual(calls, 3);
  });

  await test('client errors are readable and never include the key', async () => {
    const fetchImpl = async () => new Response(JSON.stringify({ error: { code: 'BadRequest', message: 'bad subscription-key=SECRET' } }), { status: 400, statusText: 'Bad Request' });
    const client = createMapsClient({ headers: async () => ({}) }, { fetchImpl, sleep: async () => {} });
    await assert.rejects(client.json('/x'), (e) => e instanceof MapsError && /400 Bad Request: bad subscription-key=\*\*\*/.test(e.message) && !/SECRET/.test(e.message));
  });

  // ───────────── tools with a canned Azure Maps ─────────────

  const feature = { type: 'Feature', geometry: { type: 'Point', coordinates: [-122.349, 47.6205] }, bbox: [-122.35, 47.62, -122.34, 47.63], properties: { type: 'LandmarkBuilding', confidence: 'High', address: { formattedAddress: 'Space Needle, WA', locality: 'Seattle', countryRegion: { ISO: 'US', name: 'United States' }, adminDistricts: [{ shortName: 'WA' }] } } };

  await test('geocode returns places for the agent and the app', async () => {
    const client = { json: async (p) => { assert.strictEqual(p, '/geocode'); return { features: [feature] }; } };
    const r = await callTool('geocode', { query: 'Space Needle' }, { client });
    assert(!r.isError);
    assert.strictEqual(r.structuredContent.kind, 'places');
    assert.strictEqual(r.structuredContent.places[0].name, 'Space Needle');
    assert(/longitude -122.349/.test(r.content[0].text));
  });

  await test('text places are geocoded once and reused', async () => {
    let geocodes = 0;
    const client = {
      json: async (p) => {
        if (p === '/geocode') { geocodes++; return { features: [feature] }; }
        if (p === '/route/directions') return routeResponse();
        throw new Error('unexpected ' + p);
      }
    };
    const r = await callTool('get_route_directions', { origin: { query: 'Space Needle' }, destination: { query: 'Space Needle' }, includeSteps: false }, { client });
    assert.strictEqual(geocodes, 1);
    assert.strictEqual(r.structuredContent.kind, 'route');
    assert.strictEqual(r.structuredContent.routes.length, 2);
    assert.strictEqual(r.structuredContent.routes[0].meters, 5000);
  });

  function routeResponse() {
    const path = (m, s) => ({ type: 'FeatureCollection', features: [
      { type: 'Feature', geometry: { type: 'MultiLineString', coordinates: [[[-122.34, 47.6], [-122.3, 47.62], [-122.2, 47.65]]] }, properties: { type: 'RoutePath', distanceInMeters: m, durationInSeconds: s, durationTrafficInSeconds: s + 60, legs: [] } },
      { type: 'Feature', geometry: { type: 'Point', coordinates: [-122.34, 47.6] }, properties: { type: 'Waypoint', order: { inputIndex: 0 }, instruction: { text: 'Leave', maneuverType: 'DepartStart' } } }
    ] });
    return { ...path(5000, 600), alternativeRoutes: [path(6000, 700)] };
  }

  await test('bad input comes back as a readable tool error, not a protocol error', async () => {
    const deps = { authenticate: authOk, maps: {}, client: { json: async () => ({}) } };
    const r = await handleMcp(rpc('tools/call', { name: 'get_route_range', arguments: { origin: { longitude: -122, latitude: 47 } } }), 'Bearer x', deps);
    assert.strictEqual(r.body.result.isError, true);
    assert(/timeBudgetsMinutes|timeBudgetInSec/.test(r.body.result.content[0].text));
    const unknown = await handleMcp(rpc('tools/call', { name: 'nope', arguments: {} }), 'Bearer x', deps);
    assert.strictEqual(unknown.body.error.code, -32602);
  });

  await test('the tile tool caches tiles', async () => {
    let fetched = 0;
    const client = { binary: async () => { fetched++; return { bytes: Buffer.from([0x89, 0x50, 0x4e, 0x47]), mimeType: 'image/png' }; } };
    const args = { zoom: 5, x: 3, y: 4, tilesetId: 'microsoft.base.road' };
    const a = await callTool('get_map_tile', args, { client });
    const b = await callTool('get_map_tile', args, { client });
    assert.strictEqual(a.content[0].type, 'image');
    assert.strictEqual(b.content[0].data, a.content[0].data);
    assert.strictEqual(fetched, 1);
    const bad = await callTool('get_map_tile', { zoom: 2, x: 9, y: 0 }, { client }).catch((e) => e);
    assert(bad instanceof InputError);
  });

  await test('batches run as single requests and a failed item does not stop the rest', async () => {
    const client = createMapsClient({ headers: async () => ({}) }, { fetchImpl: async () => new Response('{}'), sleep: async () => {} });
    const out = await client.batch([1, 2, 3], async (n) => { if (n === 2) throw new Error('boom'); return { n }; });
    assert.deepStrictEqual(out.batchItems, [{ n: 1 }, { error: { message: 'boom' } }, { n: 3 }]);
    const calls = [];
    const stub = { json: async (p, o) => { calls.push(p); return { features: [feature] }; }, batch: client.batch };
    const r = await callTool('batch_geocode', { addresses: ['a', 'b', 'c'] }, { client: stub });
    assert.strictEqual(r.structuredContent.summary.matched, 3);
    assert.deepStrictEqual(calls, ['/geocode', '/geocode', '/geocode']);
  });

  await test('coordinate-only points get readable names, and the names are cached', async () => {
    let lookups = 0;
    const client = { json: async () => { lookups++; return { features: [{ properties: { address: { addressLine: '1 Main St', locality: 'Town' } } }] }; } };
    const resolver = require('../lib/args').createPlaceResolver(client);
    const pts = [{ longitude: 1, latitude: 2 }, { longitude: 1, latitude: 2 }, { longitude: 3, latitude: 4, name: 'Kept' }];
    await resolver.nameAll(pts);
    assert.strictEqual(pts[0].name, '1 Main St, Town');
    assert.strictEqual(pts[2].name, 'Kept');
    assert.strictEqual(lookups, 1);
  });

  console.log(`\n${passed} passed, ${failed} failed`);
  process.exit(failed ? 1 : 0);
})();
