'use strict';

// Starts the server (or targets BASE_URL) and exercises it over HTTP with real Microsoft Entra tokens.
//   TOKEN       access token for the API (scope access_as_user)
//   BAD_AUD     optional token issued for a different audience, to prove it is rejected
//   For a local server also set TENANT_ID, CLIENT_ID and MAPS_SUBSCRIPTION_KEY.
const { spawn } = require('child_process');
const path = require('path');

const REMOTE = process.env.BASE_URL;
const PORT = 18080 + Math.floor(Math.random() * 500);
const TOKEN = process.env.TOKEN;
const BAD_AUD = process.env.BAD_AUD;
const BASE = REMOTE || `http://127.0.0.1:${PORT}`;

let failures = 0;
const check = (name, ok, detail = '') => {
  if (!ok) failures++;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  ' + detail : ''}`);
};

async function rpc(body, token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = 'Bearer ' + token;
  const res = await fetch(BASE + '/mcp', { method: 'POST', headers, body: JSON.stringify(body) });
  const text = await res.text();
  return { status: res.status, www: res.headers.get('www-authenticate'), json: text ? JSON.parse(text) : null };
}

const call = (name, args, token = TOKEN) =>
  rpc({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name, arguments: args } }, token);

const sig = (b64) => {
  const b = Buffer.from(b64, 'base64');
  if (b[0] === 0x89 && b[1] === 0x50) return 'PNG';
  if (b[0] === 0xff && b[1] === 0xd8) return 'JPEG';
  return 'UNKNOWN';
};
const size = (b64) => Buffer.from(b64, 'base64').length;
const image = (r) => r.json.result.content.find((p) => p.type === 'image');

const route = { originLongitude: -122.335167, originLatitude: 47.608013, destinationLongitude: -122.12151, destinationLatitude: 47.67399 };

(async () => {
  const server = REMOTE ? null : spawn(process.execPath, [path.join(__dirname, '..', 'server.js')], { env: { ...process.env, PORT: String(PORT) }, stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, REMOTE ? 0 : 1500));

  try {
    let r = await fetch(BASE + '/health');
    check('health', r.status === 200);

    r = await fetch(BASE + '/img/anything');
    check('there is no public image endpoint', r.status === 404, String(r.status));

    r = await rpc({ jsonrpc: '2.0', id: 1, method: 'initialize', params: {} });
    check('initialize without a token', r.json.result.serverInfo.name === 'azure-maps-route-mcp');

    r = await rpc({ jsonrpc: '2.0', method: 'notifications/initialized' });
    check('notification returns 202', r.status === 202);

    r = await rpc({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
    const tools = r.json.result.tools;
    check('tools/list has 7 tools', tools.length === 7, tools.map((t) => t.name).join(','));
    const widgetTools = tools.filter((t) => t._meta && t._meta.ui && t._meta.ui.resourceUri === 'ui://azure-maps-route/map.html').map((t) => t.name);
    check('map tools declare the widget resource', widgetTools.sort().join(',') === 'get_route_map,get_static_map', widgetTools.join(','));
    const renderTool = tools.find((t) => t.name === 'render_map_image');
    check('render_map_image is app-only', Boolean(renderTool) && JSON.stringify(renderTool._meta.ui.visibility) === '["app"]');

    r = await rpc({ jsonrpc: '2.0', id: 3, method: 'resources/read', params: { uri: 'ui://azure-maps-route/map.html' } });
    const res = r.json.result && r.json.result.contents && r.json.result.contents[0];
    check('widget resource has the MCP Apps MIME type and HTML', res && res.mimeType === 'text/html;profile=mcp-app' && /ui\/initialize/.test(res.text) && /render_map_image/.test(res.text) && /<\/html>/.test(res.text));
    check('widget declares no external domains', res && !(res._meta && res._meta.ui && res._meta.ui.csp));
    r = await rpc({ jsonrpc: '2.0', id: 4, method: 'resources/read', params: { uri: 'ui://other/x.html' } });
    check('unknown resource is an error', Boolean(r.json.error));

    // Rejections
    r = await call('get_route_map', route, null);
    check('tools/call without a token is 401', r.status === 401 && /Bearer/.test(r.www || ''));

    r = await call('render_map_image', { centerLongitude: -122.33, centerLatitude: 47.6 }, null);
    check('render_map_image without a token is 401', r.status === 401);

    r = await call('get_route_map', route, 'not-a-jwt');
    check('malformed token is 401', r.status === 401);

    if (TOKEN) {
      const parts = TOKEN.split('.');
      const tampered = parts[0] + '.' + Buffer.from(JSON.stringify({ ...JSON.parse(Buffer.from(parts[1], 'base64url')), scp: 'access_as_user', oid: 'attacker' })).toString('base64url') + '.' + parts[2];
      r = await call('get_route_map', route, tampered);
      check('token with altered claims is 401', r.status === 401, r.www || '');
    }

    if (BAD_AUD) {
      r = await call('get_route_map', route, BAD_AUD);
      check('token for another audience is 401', r.status === 401, r.www || '');
    }

    if (!TOKEN) {
      console.log('TOKEN not set, skipping authenticated checks');
    } else {
      r = await call('get_route_map', { ...route, waypoints: '', avoid: '', routeWidth: 0, markEndpoints: true, tilesetId: '', language: 'en-US' });
      const out = r.json.result;
      const text = out.content.find((p) => p.type === 'text').text;
      const sc = out.structuredContent;
      check('get_route_map returns the map request, not an image', !out.isError && out.content.length === 1 && sc && Array.isArray(sc.mapArgs.routeCoordinates), text.slice(0, 80));
      check('route line is simplified to at most 100 points', sc && sc.mapArgs.routeCoordinates.length <= 100 && sc.mapArgs.routeCoordinates.length > 10, sc ? String(sc.mapArgs.routeCoordinates.length) : '');
      check('summary and Bing link are returned', sc && /24\.\d km/.test(sc.summary) && /bing\.com\/maps\?rtp=/.test(sc.bingUrl) && text.includes(sc.bingUrl));
      check('no public image link in the result', !/\/img\//.test(JSON.stringify(out)) && !/!\[Map\]/.test(text));
      check('result stays far under the 64 KiB widget limit', JSON.stringify(out).length < 16 * 1024, String(JSON.stringify(out).length));

      // The widget's call, made as the signed-in user
      r = await call('render_map_image', sc.mapArgs);
      let img = r.json.result && image(r);
      check('render_map_image returns a small JPEG', img && sig(img.data) === 'JPEG' && size(img.data) <= 150 * 1024, img ? size(img.data) + ' bytes' : JSON.stringify(r.json).slice(0, 120));

      r = await call('render_map_image', { ...sc.mapArgs, format: 'png', width: 1800, height: 1400 });
      img = r.json.result && image(r);
      check('large PNG request is reduced under the limit', img && size(img.data) <= 150 * 1024, img ? size(img.data) + ' bytes ' + img.mimeType : '');

      r = await call('render_map_image', {});
      check('render_map_image with nothing is a clear error', r.json.result.isError && /centerLongitude/.test(r.json.result.content[0].text));

      r = await call('get_route_map', { ...route, embedImage: true });
      check('embedImage also returns the image data', r.json.result.content.some((p) => p.type === 'image' && sig(p.data) === 'JPEG') && r.json.result.content[0].type === 'text');

      r = await call('get_static_map', { centerLongitude: -122.33, centerLatitude: 47.6, zoom: 12, width: 300, height: 200, bbox: [], pins: [], path: [], tilesetId: '' });
      check('get_static_map returns a request and a Bing link', !r.json.result.isError && r.json.result.structuredContent.mapArgs.centerLatitude === 47.6 && /bing\.com\/maps\?cp=/.test(r.json.result.structuredContent.bingUrl));

      r = await call('get_static_map', { centerLongitude: -122.33, centerLatitude: 47.6, zoom: 12, width: 300, height: 200, format: 'png', embedImage: true });
      img = image(r);
      check('get_static_map can embed a PNG', img && sig(img.data) === 'PNG');

      r = await call('get_static_map', {});
      check('get_static_map with nothing is a clear error', r.json.result.isError && /centerLongitude/.test(r.json.result.content[0].text));

      r = await call('get_route_directions', { ...route, waypoints: '-122.2,47.62', avoid: 'tollRoads', routeOutputOptions: 'routeSummary' });
      const fc = JSON.parse(r.json.result.content[1].text);
      check('get_route_directions FeatureCollection, link first', fc.type === 'FeatureCollection' && /bing\.com\/maps\?rtp=/.test(r.json.result.content[0].text));

      r = await call('get_route_directions', { ...route, maxRouteCount: 3, routeOutputOptions: 'routeSummary,routePath,itinerary' });
      const big = r.json.result.content[1].text;
      check('3 routes with itinerary stay under the size cap', !r.json.result.isError && big.length <= 70000, big.length + ' chars');

      r = await call('get_route_matrix', { origins: '-122.335167,47.608013', destinations: '-122.12151,47.67399;-122.2,47.45' });
      check('get_route_matrix 2 cells', JSON.parse(r.json.result.content[0].text).properties.matrix.length === 2);

      r = await call('get_route_range', { originLongitude: -122.335167, originLatitude: 47.608013, timeBudgetInSec: 900 });
      check('get_route_range polygon', JSON.parse(r.json.result.content[0].text).type === 'FeatureCollection');

      r = await call('snap_to_roads', { points: '-122.335167,47.608013;-122.3321,47.6062', includeSpeedLimit: true });
      check('snap_to_roads', !r.json.result.isError);
    }
  } finally {
    if (server) server.kill();
  }

  console.log(failures ? `\n${failures} check(s) failed` : '\nAll checks passed');
  process.exit(failures ? 1 : 0);
})();
