'use strict';

const fs = require('fs');
const path = require('path');

const MAPS_HOST = 'https://atlas.microsoft.com';
const ROUTE_API_VERSION = '2025-01-01';
const RENDER_API_VERSION = '2024-04-01';
const GEOJSON = 'application/geo+json';
const MAX_IMAGE_BYTES = 150 * 1024;
const MAX_DIRECTIONS_CHARS = 60000;
const DEFAULT_IMAGE_WIDTH = 640;
const DEFAULT_IMAGE_HEIGHT = 400;

const TRAVEL_MODES = ['driving', 'truck', 'walking'];
const OPTIMIZE_MODES = ['short', 'shortest', 'fastestWithoutTraffic', 'fastestWithTraffic'];
const AVOID_VALUES = ['limitedAccessHighways', 'tollRoads', 'ferries', 'tunnels', 'borderCrossings', 'lowEmissionZones', 'unpavedRoads'];
const MAP_STYLES = ['microsoft.base.road', 'microsoft.base.darkgrey', 'microsoft.imagery'];

// ───────────────────────────── helpers ─────────────────────────────

const isBlank = (v) =>
  v === null || v === undefined ||
  (typeof v === 'string' && v.trim() === '') ||
  (Array.isArray(v) && v.length === 0);

const ZERO_MEANS_BLANK = new Set(['maxRouteCount', 'routeWidth', 'width', 'height', 'timeBudgetInSec', 'distanceBudgetInMeters']);

/** Copilot sends every optional input; drop the ones the agent left blank. */
function removeBlankArguments(args) {
  const out = {};
  for (const [k, v] of Object.entries(args || {})) {
    if (isBlank(v)) continue;
    if (typeof v === 'number' && ZERO_MEANS_BLANK.has(k) && v === 0) continue;
    out[k] = v;
  }
  return out;
}

function requireNumber(source, name) {
  const v = source[name];
  if (v === undefined || v === null || v === '' || Number.isNaN(Number(v))) {
    throw new Error('Missing required argument: ' + name);
  }
  return Number(v);
}

/** Turns "lon,lat;lon,lat" text into [{longitude, latitude}] and comma separated text into arrays. */
function normalizeInput(input) {
  const result = { ...input };

  for (const name of ['waypoints', 'origins', 'destinations', 'points']) {
    if (typeof result[name] !== 'string') continue;
    result[name] = result[name].split(';').filter((s) => s.trim()).map((pair) => {
      const parts = pair.split(',');
      const longitude = Number(parts[0]);
      const latitude = Number(parts[1]);
      if (parts.length !== 2 || Number.isNaN(longitude) || Number.isNaN(latitude)) {
        throw new Error(name + ' must be longitude,latitude pairs separated by semicolons, for example -122.33,47.6;-122.12,47.67. Got: ' + pair);
      }
      return { longitude, latitude };
    });
  }

  for (const name of ['avoid', 'routeOutputOptions']) {
    if (typeof result[name] === 'string') {
      result[name] = result[name].split(',').map((s) => s.trim()).filter(Boolean);
    }
  }
  return result;
}

function copyIfPresent(source, target, names) {
  for (const n of names) if (source[n] !== undefined && source[n] !== null) target[n] = source[n];
}

const pointFeature = (lon, lat, properties = {}) => ({
  type: 'Feature',
  geometry: { type: 'Point', coordinates: [lon, lat] },
  properties
});

const waypointFeature = (lon, lat, index) => pointFeature(lon, lat, { pointIndex: index, pointType: 'waypoint' });

function toPositions(list, name) {
  if (!Array.isArray(list) || list.length === 0) throw new Error('Missing required argument: ' + name);
  return list.map((p) => [requireNumber(p, 'longitude'), requireNumber(p, 'latitude')]);
}

// ───────────────────────── request builders ─────────────────────────

function buildDirectionsBody(raw) {
  const input = normalizeInput(raw);
  const features = [];
  let index = 0;
  features.push(waypointFeature(requireNumber(input, 'originLongitude'), requireNumber(input, 'originLatitude'), index++));
  for (const stop of Array.isArray(input.waypoints) ? input.waypoints : []) {
    features.push(waypointFeature(requireNumber(stop, 'longitude'), requireNumber(stop, 'latitude'), index++));
  }
  features.push(waypointFeature(requireNumber(input, 'destinationLongitude'), requireNumber(input, 'destinationLatitude'), index));

  const body = { type: 'FeatureCollection', features };
  copyIfPresent(input, body, ['travelMode', 'optimizeRoute', 'optimizeWaypointOrder', 'departAt', 'arriveAt', 'avoid', 'routeOutputOptions', 'maxRouteCount']);
  if (!body.routeOutputOptions) body.routeOutputOptions = ['routeSummary'];
  return body;
}

function buildMatrixBody(raw) {
  const input = normalizeInput(raw);
  const features = ['origins', 'destinations'].map((kind) => ({
    type: 'Feature',
    geometry: { type: 'MultiPoint', coordinates: toPositions(input[kind], kind) },
    properties: { pointType: kind }
  }));
  const body = { type: 'FeatureCollection', features, optimizeRoute: 'fastest' };
  copyIfPresent(input, body, ['travelMode', 'departAt', 'arriveAt', 'traffic', 'avoid']);
  return body;
}

function buildRangeBody(raw) {
  const input = normalizeInput(raw);
  const properties = {};
  copyIfPresent(input, properties, ['timeBudgetInSec', 'distanceBudgetInMeters', 'travelMode', 'optimizeRoute', 'departAt', 'avoid']);
  if (properties.timeBudgetInSec === undefined && properties.distanceBudgetInMeters === undefined) {
    throw new Error('Provide timeBudgetInSec or distanceBudgetInMeters.');
  }
  return pointFeature(requireNumber(input, 'originLongitude'), requireNumber(input, 'originLatitude'), properties);
}

function buildSnapBody(raw) {
  const input = normalizeInput(raw);
  const features = toPositions(input.points, 'points').map(([lon, lat]) => pointFeature(lon, lat));
  const body = { type: 'FeatureCollection', features };
  copyIfPresent(input, body, ['travelMode', 'interpolate', 'includeSpeedLimit']);
  return body;
}

// ───────────────────────────── Azure Maps ─────────────────────────────

class MapsError extends Error {}

async function callMapsGeoJson(path, body, maps, language) {
  const headers = { 'Content-Type': GEOJSON, Accept: GEOJSON + ', application/json;q=0.5', ...(await maps.headers()) };
  if (language) headers['Accept-Language'] = language;

  const response = await fetch(`${MAPS_HOST}${path}?api-version=${ROUTE_API_VERSION}`, {
    method: 'POST',
    headers,
    body: JSON.stringify(body)
  });
  const text = await response.text();
  if (!response.ok) throw new MapsError(`Azure Maps returned ${response.status} ${response.statusText}: ${text}`);
  return text;
}

// ───────────────────────────── static maps ─────────────────────────────

const isJpeg = (f) => ['jpeg', 'jpg'].includes(String(f || '').toLowerCase());

function flattenPositions(token, out) {
  if (!Array.isArray(token) || token.length === 0) return;
  if (typeof token[0] === 'number') {
    if (token.length >= 2) out.push([token[0], token[1]]);
    return;
  }
  for (const child of token) flattenPositions(child, out);
}

const mercatorY = (lat) => Math.log(Math.tan(Math.PI / 4 + (Math.max(-85, Math.min(85, lat)) * Math.PI) / 360));

function fitZoom(minLon, minLat, maxLon, maxLat, width, height) {
  const padding = 1.2;
  const lonSpan = (maxLon - minLon) * padding;
  const ySpan = (mercatorY(maxLat) - mercatorY(minLat)) * padding;
  if (lonSpan <= 0 && ySpan <= 0) return 14;
  let zoom = 20;
  if (lonSpan > 0) zoom = Math.min(zoom, Math.log2((width * 360) / (512 * lonSpan)));
  if (ySpan > 0) zoom = Math.min(zoom, Math.log2((height * 2 * Math.PI) / (512 * ySpan)));
  return Math.max(0, Math.min(20, Math.floor(zoom)));
}

const fmt = (n) => Number(n.toFixed(6)).toString();
const pos = (lon, lat) => `${fmt(lon)} ${fmt(lat)}`;

function bingCenterUrl(centerText, zoom) {
  const [lon, lat] = String(centerText).split(',');
  if (lon === undefined || lat === undefined || Number.isNaN(Number(lon)) || Number.isNaN(Number(lat))) return null;
  return `https://www.bing.com/maps?cp=${lat}~${lon}&lvl=${zoom}`;
}

function bingCenterHint(centerText, zoom) {
  const url = bingCenterUrl(centerText, zoom);
  return url ? ` Share this link so the user can open the area: ${url}` : '';
}

function bingRouteUrl(rawArgs) {
  const input = normalizeInput(rawArgs);
  const p = (lat, lon) => `pos.${fmt(lat)}_${fmt(lon)}`;
  const stops = [p(requireNumber(input, 'originLatitude'), requireNumber(input, 'originLongitude'))];
  for (const s of Array.isArray(input.waypoints) ? input.waypoints : []) stops.push(p(requireNumber(s, 'latitude'), requireNumber(s, 'longitude')));
  stops.push(p(requireNumber(input, 'destinationLatitude'), requireNumber(input, 'destinationLongitude')));
  return `https://www.bing.com/maps?rtp=${stops.join('~')}&mode=${input.travelMode === 'walking' ? 'W' : 'D'}`;
}

function buildStaticMapRequest(args) {
  let width = args.width ?? DEFAULT_IMAGE_WIDTH;
  let height = args.height ?? DEFAULT_IMAGE_HEIGHT;
  const pins = [...(Array.isArray(args.pins) ? args.pins.map(String) : [])];
  const paths = [...(Array.isArray(args.path) ? args.path.map(String) : [])];
  const query = [['api-version', RENDER_API_VERSION]];

  const route = [];
  flattenPositions(args.routeCoordinates, route);

  let bbox = [];
  if (Array.isArray(args.bbox)) bbox = args.bbox.map(Number);
  else if (typeof args.bbox === 'string') bbox = args.bbox.split(',').map(Number).filter((n) => !Number.isNaN(n));

  const hasCenter = args.centerLongitude !== undefined;
  let centerText;
  let zoomValue;

  if (route.length > 0) {
    let points = route;
    if (points.length > 100) {
      points = Array.from({ length: 100 }, (_, i) => route[Math.round((i * (route.length - 1)) / 99)]);
    }
    const color = String(args.routeColor || '0078D4').replace(/^#/, '');
    const lineWidth = Math.max(1, Math.min(50, args.routeWidth ?? 5));
    paths.push(`lc${color}|lw${lineWidth}||${points.map(([lo, la]) => pos(lo, la)).join('|')}`);

    if (args.markEndpoints === undefined || args.markEndpoints) {
      pins.push(`default|co2E7D32||'A'${pos(route[0][0], route[0][1])}`);
      const last = route[route.length - 1];
      pins.push(`default|coC62828||'B'${pos(last[0], last[1])}`);
    }

    if (!hasCenter) {
      const lons = route.map((p) => p[0]);
      const lats = route.map((p) => p[1]);
      const minLon = Math.min(...lons), maxLon = Math.max(...lons), minLat = Math.min(...lats), maxLat = Math.max(...lats);
      zoomValue = args.zoom ?? fitZoom(minLon, minLat, maxLon, maxLat, width, height);
      centerText = `${fmt((minLon + maxLon) / 2)},${fmt((minLat + maxLat) / 2)}`;
      query.push(['center', centerText], ['zoom', String(zoomValue)]);
    }
  }

  if (route.length === 0 || hasCenter) {
    if (bbox.length === 4) {
      query.push(['bbox', bbox.join(',')]);
      centerText = bbox.join(',');
      width = 0;
      height = 0;
    } else {
      const lon = requireNumber(args, 'centerLongitude');
      const lat = requireNumber(args, 'centerLatitude');
      zoomValue = args.zoom ?? 12;
      centerText = `${fmt(lon)},${fmt(lat)}`;
      query.push(['center', centerText], ['zoom', String(zoomValue)]);
    }
  }

  for (const name of ['tilesetId', 'trafficLayer', 'language', 'view']) {
    if (args[name]) query.push([name, String(args[name])]);
  }
  for (const p of pins) query.push(['pins', p]);
  for (const p of paths) query.push(['path', p]);

  // Large images get compacted away by some clients, so keep results small: JPEG by default.
  const jpeg = args.format === undefined ? true : isJpeg(args.format);
  return { query, width, height, jpeg, centerText, zoomValue };
}

/** Fetches the image from Azure Maps and shrinks it in steps until it is under the size cap. */
async function renderStaticMap(request, maps) {
  const { query } = request;
  let { width, height, jpeg } = request;
  let shrunk = false;
  let bytes;
  let mimeType;

  for (let attempt = 0; attempt < 6; attempt++) {
    const q = [...query];
    if (width > 0) q.push(['width', String(width)]);
    if (height > 0) q.push(['height', String(height)]);
    const url = `${MAPS_HOST}/map/static?` + q.map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&');

    const response = await fetch(url, { headers: { Accept: jpeg ? 'image/jpeg' : 'image/png', ...(await maps.headers()) } });
    if (!response.ok) throw new MapsError(`Azure Maps returned ${response.status} ${response.statusText}: ${await response.text()}`);

    bytes = Buffer.from(await response.arrayBuffer());
    mimeType = (response.headers.get('content-type') || (jpeg ? 'image/jpeg' : 'image/png')).split(';')[0];
    if (bytes.length <= MAX_IMAGE_BYTES) break;

    if (!jpeg) {
      jpeg = true;
    } else if (width > 0 && height > 0) {
      width = Math.max(80, Math.round(width * 0.75));
      height = Math.max(80, Math.round(height * 0.75));
      shrunk = true;
    } else {
      break;
    }
  }

  return { bytes, mimeType, shrunk, width, height };
}

/** Evenly samples a list of positions down to at most `max`, keeping the first and last. */
function samplePositions(positions, max) {
  if (positions.length <= max) return positions;
  return Array.from({ length: max }, (_, i) => positions[Math.round((i * (positions.length - 1)) / (max - 1))]);
}

/**
 * Validates the map request and returns it for the map widget to render, with a Bing Maps link. The image isn't
 * rendered here. The widget asks for it through the host with the app-only render_map_image tool, which carries the
 * user's sign-in, so no public image link exists. Clients without widget support get the text and the Bing link.
 * Set embedImage to also return the image bytes, for clients that render MCP image content.
 */
async function staticMap(args, maps, extraText, structured = {}) {
  const request = buildStaticMapRequest(args);
  const { centerText, zoomValue } = request;
  const centerBing = !extraText && zoomValue !== undefined ? bingCenterUrl(centerText, zoomValue) : null;

  const parts = [];
  if (extraText) parts.push(extraText);
  parts.push('A map widget shows the map when the client supports it. If the user cannot see a map, share the Bing Maps link instead; do not describe or draw a map yourself.');
  if (centerBing) parts.push(`Bing Maps link for this area: ${centerBing}`);

  const content = [{ type: 'text', text: parts.join(' ') }];
  if (args.embedImage) {
    const rendered = await renderStaticMap(request, maps);
    content.push({ type: 'image', data: rendered.bytes.toString('base64'), mimeType: rendered.mimeType });
  }

  const { embedImage, ...mapArgs } = args;
  return {
    content,
    structuredContent: {
      mapArgs,
      summary: structured.summary || 'Map',
      bingUrl: structured.bingUrl || centerBing || undefined,
      alt: structured.summary || 'Map'
    },
    isError: false
  };
}

/** App-only: renders the image for the widget and returns it as image content. */
async function renderMapImage(args, maps) {
  const request = buildStaticMapRequest(args);
  const rendered = await renderStaticMap(request, maps);
  return {
    content: [
      { type: 'image', data: rendered.bytes.toString('base64'), mimeType: rendered.mimeType },
      { type: 'text', text: `Map image (${rendered.mimeType}, ${rendered.bytes.length} bytes).` }
    ],
    isError: false
  };
}

async function routeMap(args, maps) {
  const directionsArgs = { ...args, routeOutputOptions: ['routeSummary', 'routePath'] };
  const json = JSON.parse(await callMapsGeoJson('/route/directions', buildDirectionsBody(directionsArgs), maps, args.language));
  const feature = (json.features || []).find((f) => f.properties && f.properties.type === 'RoutePath');
  if (!feature) throw new Error('No route was found between those points.');

  const line = [];
  flattenPositions(feature.geometry.coordinates, line);
  const mapArgs = { routeCoordinates: samplePositions(line, 100) };
  copyIfPresent(args, mapArgs, ['routeColor', 'routeWidth', 'markEndpoints', 'width', 'height', 'zoom', 'tilesetId', 'trafficLayer', 'language', 'view', 'format', 'embedImage']);

  const km = (feature.properties.distanceInMeters || 0) / 1000;
  const minutes = Math.round((feature.properties.durationInSeconds || 0) / 60);
  const bingUrl = bingRouteUrl(args);
  const summary = `Route: ${km.toFixed(1)} km, about ${minutes} minutes. Bing Maps link for this route: ${bingUrl}`;
  return staticMap(mapArgs, maps, summary, { summary: `${km.toFixed(1)} km, about ${minutes} minutes`, bingUrl });
}

// ───────────────────────────── tools ─────────────────────────────

const str = (description) => ({ type: 'string', description });
const num = (description) => ({ type: 'number', description });
const int = (description) => ({ type: 'integer', description });
const bool = (description) => ({ type: 'boolean', description });
const enumOf = (description, values) => ({ type: 'string', description, enum: values });
const pairs = 'longitude,latitude pairs separated by semicolons, for example -122.33,47.6;-122.12,47.67';
const avoidText = 'Road features to avoid, comma-separated. Values: ' + AVOID_VALUES.join(', ') + '.';

function tool(name, description, properties, required, meta) {
  const inputSchema = { type: 'object', properties };
  if (required.length) inputSchema.required = required;
  return { name, description, inputSchema, ...(meta ? { _meta: meta } : {}) };
}

const WIDGET_URI = 'ui://azure-maps-route/map.html';
const WIDGET_MIME = 'text/html;profile=mcp-app';
const WIDGET_HTML = fs.readFileSync(path.join(__dirname, 'widget.html'), 'utf8');
const MAP_WIDGET_META = { ui: { resourceUri: WIDGET_URI } };

/** The map widget. It loads nothing from the network: the image arrives as a data: URL through the host. */
function widgetResource() {
  return { uri: WIDGET_URI, mimeType: WIDGET_MIME, text: WIDGET_HTML, _meta: { ui: { prefersBorder: false } } };
}

const MAP_OPTIONS = {
  width: int('Image width in pixels, 80 to 2000. Default: 640. Larger images are reduced automatically to keep the result small.'),
  height: int('Image height in pixels, 80 to 1500. Default: 400.'),
  tilesetId: enumOf('Map style. Default: microsoft.base.road.', MAP_STYLES),
  trafficLayer: enumOf('Show live traffic. Default: none.', ['microsoft.traffic.relative.main', 'none']),
  format: enumOf('Image format. Default: jpeg, which is much smaller. Use png only when needed.', ['jpeg', 'png']),
  language: str('Language, for example en-US.'),
  embedImage: bool('Also return the image data in the result. Default: false. The map widget loads the image itself, so only set this for a client that renders MCP image content but has no widget support.')
};

const TOOLS = [
  tool('get_route_directions',
    'Get the best route between an origin and destination (with optional stops) as a GeoJSON FeatureCollection. The route line is a MultiLineString feature; waypoints and turn-by-turn maneuvers are Point features. Coordinates are [longitude, latitude].',
    {
      originLongitude: num('Longitude of the origin (-180 to 180).'),
      originLatitude: num('Latitude of the origin (-90 to 90).'),
      destinationLongitude: num('Longitude of the destination (-180 to 180).'),
      destinationLatitude: num('Latitude of the destination (-90 to 90).'),
      waypoints: str('Optional stops between origin and destination, in order, as ' + pairs + '.'),
      travelMode: enumOf('Mode of travel. Default: driving.', TRAVEL_MODES),
      optimizeRoute: enumOf('Route optimization. Default: fastestWithoutTraffic.', OPTIMIZE_MODES),
      optimizeWaypointOrder: bool('Reorder intermediate waypoints to reduce route cost.'),
      departAt: str('Departure time, ISO 8601. Cannot be combined with arriveAt.'),
      arriveAt: str('Arrival time, ISO 8601. Cannot be combined with departAt.'),
      avoid: str(avoidText),
      routeOutputOptions: str('Data to return, comma-separated. Default: routeSummary (distance and time). Add routePath only when the route line is needed, and itinerary only when the user asks for turn-by-turn instructions; both make the response much larger. To show a route on a map, use get_route_map instead.'),
      maxRouteCount: int('Number of routes to return, including alternatives. Default: 1. Each extra route adds a large amount of data, so ask for more only when the user wants alternatives.'),
      language: str('Language for instructions, for example en-US.')
    },
    ['originLongitude', 'originLatitude', 'destinationLongitude', 'destinationLatitude']),

  tool('get_route_matrix',
    'Calculate travel time and distance between every origin and every destination. Origins multiplied by destinations must not exceed 2,500, and larger matrices have extra restrictions (see the Azure Maps Route Matrix documentation). Returns a GeoJSON Feature with a summary per origin and destination pair.',
    {
      origins: str('Origin points as ' + pairs + '.'),
      destinations: str('Destination points as ' + pairs + '.'),
      travelMode: enumOf('Mode of travel. Default: driving.', TRAVEL_MODES),
      traffic: enumOf('Traffic model. Default: historical.', ['historical', 'live']),
      departAt: str('Departure time, ISO 8601.'),
      arriveAt: str('Arrival time, ISO 8601.'),
      avoid: str('Road features to avoid, comma-separated. Values: tollRoads, unpavedRoads.')
    },
    ['origins', 'destinations']),

  tool('get_route_range',
    'Get the area reachable from an origin within a time or distance budget (an isochrone), returned as a GeoJSON Polygon feature.',
    {
      originLongitude: num('Longitude of the starting point (-180 to 180).'),
      originLatitude: num('Latitude of the starting point (-90 to 90).'),
      timeBudgetInSec: num('Time budget in seconds. Provide this or distanceBudgetInMeters.'),
      distanceBudgetInMeters: num('Distance budget in meters. Provide this or timeBudgetInSec.'),
      travelMode: enumOf('Mode of travel. Default: driving.', ['driving', 'truck']),
      optimizeRoute: enumOf('Route optimization. Default: fastestWithoutTraffic.', ['shortest', 'fastestWithoutTraffic', 'fastestWithTraffic']),
      departAt: str('Departure time, ISO 8601.'),
      avoid: str(avoidText)
    },
    ['originLongitude', 'originLatitude']),

  tool('snap_to_roads',
    'Snap GPS points to the road network and return the matched route as a GeoJSON FeatureCollection. Optionally interpolate between points and include speed limits.',
    {
      points: str('GPS points in travel order as ' + pairs + '.'),
      travelMode: enumOf('Mode of travel. Default: driving.', ['driving', 'truck']),
      interpolate: bool('Add points along the road between the snapped points.'),
      includeSpeedLimit: bool('Include speed limits in kilometers per hour.'),
      language: str('Language for road names, for example en-US.')
    },
    ['points']),

  tool('get_route_map',
    'Get driving, truck, or walking directions and draw the route on a map in one step. Returns the distance and travel time, a Bing Maps link, and a map widget that shows the route. Use this when the user asks to see a route on a map. Coordinates are longitude and latitude.',
    {
      originLongitude: num('Longitude of the origin (-180 to 180).'),
      originLatitude: num('Latitude of the origin (-90 to 90).'),
      destinationLongitude: num('Longitude of the destination (-180 to 180).'),
      destinationLatitude: num('Latitude of the destination (-90 to 90).'),
      waypoints: str('Optional stops between origin and destination, in order, as ' + pairs + '.'),
      travelMode: enumOf('Mode of travel. Default: driving.', TRAVEL_MODES),
      optimizeRoute: enumOf('Route optimization. Default: fastestWithoutTraffic.', OPTIMIZE_MODES),
      avoid: str(avoidText),
      departAt: str('Departure time, ISO 8601.'),
      routeColor: str('Route line color as six hex digits without #. Default: 0078D4.'),
      routeWidth: int('Route line width in pixels, 1 to 50. Default: 5.'),
      markEndpoints: bool('Add pins labeled A and B at the start and end. Default: true.'),
      ...MAP_OPTIONS
    },
    ['originLongitude', 'originLatitude', 'destinationLongitude', 'destinationLatitude'], MAP_WIDGET_META),

  tool('get_static_map',
    'Show a static map (PNG or JPEG) with optional pushpins, lines, polygons, and live traffic, through a map widget. Give a center point and zoom, or a bbox. Returns a Bing Maps link for the area. To show a route, use get_route_map instead.',
    {
      centerLongitude: num('Longitude of the map center (-180 to 180). Required unless bbox is given.'),
      centerLatitude: num('Latitude of the map center (-90 to 90).'),
      zoom: int('Zoom level 0 (world) to 20 (street). Default: 12.'),
      bbox: str('Area to show as minLon,minLat,maxLon,maxLat. Image size is then set by the service.'),
      ...MAP_OPTIONS,
      pins: { type: 'array', description: "Pushpin definitions, for example default|co0078D4||'1'-122.33 47.6. Longitude comes before latitude.", items: { type: 'string' } },
      path: { type: 'array', description: 'Line, polygon, or circle definitions, for example lcFF0000|lw3||-122.33 47.6|-122.12 47.67.', items: { type: 'string' } }
    },
    [], MAP_WIDGET_META),

  // App-only: called by the map widget through the host, hidden from the agent.
  tool('render_map_image',
    'Renders the map image for the map widget. Only the map widget calls this; the agent should use get_route_map or get_static_map.',
    {
      routeCoordinates: { type: 'array', description: 'Route line as [longitude, latitude] positions.', items: { type: 'array', items: { type: 'number' } } },
      routeColor: str('Route line color as six hex digits without #.'),
      routeWidth: int('Route line width in pixels.'),
      markEndpoints: bool('Add pins labeled A and B at the start and end.'),
      centerLongitude: num('Longitude of the map center.'),
      centerLatitude: num('Latitude of the map center.'),
      zoom: int('Zoom level 0 to 20.'),
      bbox: str('Area to show as minLon,minLat,maxLon,maxLat.'),
      width: int('Image width in pixels.'),
      height: int('Image height in pixels.'),
      tilesetId: enumOf('Map style.', MAP_STYLES),
      trafficLayer: enumOf('Live traffic layer.', ['microsoft.traffic.relative.main', 'none']),
      format: enumOf('Image format.', ['jpeg', 'png']),
      language: str('Language for labels.'),
      pins: { type: 'array', description: 'Pushpin definitions.', items: { type: 'string' } },
      path: { type: 'array', description: 'Line, polygon, or circle definitions.', items: { type: 'string' } }
    },
    [], { ui: { visibility: ['app'] } })
];

async function callTool(name, rawArgs, maps) {
  const args = removeBlankArguments(rawArgs);
  const language = args.language;
  const text = (t) => ({ content: [{ type: 'text', text: t }], isError: false });

  switch (name) {
    case 'get_route_directions': {
      let json = await callMapsGeoJson('/route/directions', buildDirectionsBody(args), maps, language);
      let note = '';
      if (json.length > MAX_DIRECTIONS_CHARS) {
        const parsed = JSON.parse(json);
        for (const f of parsed.features || []) if (f.geometry && f.geometry.type === 'MultiLineString') f.geometry.coordinates = [];
        for (const alt of parsed.alternativeRoutes || []) for (const f of alt.features || []) if (f.geometry && f.geometry.type === 'MultiLineString') f.geometry.coordinates = [];
        json = JSON.stringify(parsed);
        note = ' The route line coordinates were removed because the result was too large. Use get_route_map to draw the route.';
      }
      return {
        content: [
          { type: 'text', text: 'To let the user see this route on a map, share this link: ' + bingRouteUrl(args) + note },
          { type: 'text', text: json }
        ],
        isError: false
      };
    }
    case 'get_route_matrix': return text(await callMapsGeoJson('/route/matrix', buildMatrixBody(args), maps));
    case 'get_route_range': return text(await callMapsGeoJson('/route/range', buildRangeBody(args), maps));
    case 'snap_to_roads': return text(await callMapsGeoJson('/route/snapToRoads', buildSnapBody(args), maps, language));
    case 'get_static_map': return staticMap(args, maps);
    case 'get_route_map': return routeMap(args, maps);
    case 'render_map_image': return renderMapImage(args, maps);
    default: return null;
  }
}

// ───────────────────────────── JSON-RPC ─────────────────────────────

const rpcResult = (id, result) => ({ status: 200, body: { jsonrpc: '2.0', result, id } });
const rpcError = (id, code, message, data) => ({
  status: 200,
  body: { jsonrpc: '2.0', error: { code, message, ...(data ? { data } : {}) }, id }
});

function wwwAuthenticate(reason) {
  return `Bearer realm="azure-maps-route-mcp"${reason ? `, error="invalid_token", error_description="${reason}"` : ''}`;
}

/**
 * Handles one JSON-RPC message. Discovery (initialize, tools/list, ping) is open so clients can connect and read the
 * public tool descriptions. tools/call requires a valid Microsoft Entra access token (deps.authenticate), and Azure Maps
 * is reached with the server's own identity (deps.maps), so callers never handle an Azure Maps key.
 */
async function handleMcp(message, authorization, deps) {
  if (!message || Array.isArray(message) || message.jsonrpc !== '2.0' || typeof message.method !== 'string') {
    return rpcError(message && message.id !== undefined ? message.id : null, -32600, 'Invalid Request');
  }

  const { method, id, params = {} } = message;
  const isNotification = id === undefined;

  switch (method) {
    case 'initialize':
      return rpcResult(id, {
        protocolVersion: '2025-03-26',
        capabilities: { tools: { listChanged: false }, resources: { listChanged: false } },
        serverInfo: { name: 'azure-maps-route-mcp', version: '2.1.0' }
      });
    case 'notifications/initialized':
    case 'notifications/cancelled':
      return { status: 202 };
    case 'ping':
      return rpcResult(id, {});
    case 'tools/list':
      return rpcResult(id, { tools: TOOLS });
    case 'resources/list':
      return rpcResult(id, { resources: [{ uri: WIDGET_URI, name: 'Map', description: 'Map view for route and static map results.', mimeType: WIDGET_MIME }] });
    case 'resources/read': {
      if (params.uri !== WIDGET_URI) return rpcError(id, -32002, 'Resource not found', params.uri);
      return rpcResult(id, { contents: [widgetResource()] });
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
      try {
        const result = await callTool(params.name, params.arguments || {}, deps.maps);
        if (!result) return rpcError(id, -32601, 'Unknown tool', params.name);
        return rpcResult(id, result);
      } catch (err) {
        return rpcResult(id, { content: [{ type: 'text', text: err.message }], isError: true });
      }
    }
    default:
      if (isNotification) return { status: 202 };
      return rpcError(id, -32601, 'Method not found', method);
  }
}

module.exports = { handleMcp, renderStaticMap, TOOLS, removeBlankArguments, normalizeInput, bingRouteUrl };
