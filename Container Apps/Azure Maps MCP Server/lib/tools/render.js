'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult } = require('../define');
const { str, num, int, bool, enumOf, arrayOf, strings, place, places } = require('../schema');
const { InputError, intInRange, oneOf, list, labelOf, parsePlace } = require('../args');
const { flatten, bboxOf, round, roundLine, simplifyLine } = require('../geo');

const TILESETS = [
  'microsoft.base', 'microsoft.base.labels', 'microsoft.base.hybrid', 'microsoft.terra.main', 'microsoft.base.road', 'microsoft.base.darkgrey',
  'microsoft.base.labels.road', 'microsoft.base.labels.darkgrey', 'microsoft.base.hybrid.road', 'microsoft.base.hybrid.darkgrey', 'microsoft.imagery',
  'microsoft.weather.radar.main', 'microsoft.weather.infrared.main', 'microsoft.traffic.absolute', 'microsoft.traffic.absolute.main', 'microsoft.traffic.relative',
  'microsoft.traffic.relative.main', 'microsoft.traffic.relative.dark', 'microsoft.traffic.delay', 'microsoft.traffic.delay.main', 'microsoft.traffic.reduced.main', 'microsoft.traffic.incident'
];
const STATIC_STYLES = ['microsoft.base.road', 'microsoft.base.darkgrey', 'microsoft.imagery'];
const MAP_STYLES = ['road', 'dark', 'satellite', 'hybrid', 'terrain'];
const VIEWS = str('Region view for disputed borders, an ISO 3166-1 alpha-2 code such as US, or Auto.');
const PALETTE = ['0078D4', 'F7630C', '8764B8', '038387', 'C239B3', '986F0B', 'D13438', '498205'];

// ───────────────────────────── tile cache ─────────────────────────────

const MAX_TILES = 2500;
const tileCache = new Map();

function tileTtlMs(tilesetId) {
  if (tilesetId.startsWith('microsoft.traffic')) return 60 * 1000;
  if (tilesetId.startsWith('microsoft.weather')) return 4 * 60 * 1000;
  return 6 * 60 * 60 * 1000;
}

const inFlightTiles = new Map();

/** Fetches one raster tile. Tiles are the same for every user, so they are cached to save Azure Maps transactions. */
async function fetchTile(ctx, { tilesetId, zoom, x, y, tileSize = 256, timeStamp, language, view }) {
  const key = [tilesetId, zoom, x, y, tileSize, timeStamp || '', language || '', view || ''].join('|');
  const hit = tileCache.get(key);
  if (hit && hit.expires > Date.now()) {
    tileCache.delete(key);
    tileCache.set(key, hit);
    return hit.tile;
  }
  // Several users asking for the same tile at once share one request.
  if (inFlightTiles.has(key)) return inFlightTiles.get(key);
  const pending = ctx.maps.binary('/map/tile', {
    version: VERSIONS.render, accept: 'image/png, image/jpeg', query: { tilesetId, zoom, x, y, tileSize, timeStamp, language, view }
  }).then((tile) => {
    tileCache.set(key, { tile, expires: Date.now() + tileTtlMs(tilesetId) });
    while (tileCache.size > MAX_TILES) tileCache.delete(tileCache.keys().next().value);
    return tile;
  }).finally(() => inFlightTiles.delete(key));
  inFlightTiles.set(key, pending);
  return pending;
}

const imageContent = ({ bytes, mimeType }) => ({ type: 'image', data: bytes.toString('base64'), mimeType });

// ───────────────────────────── map input ─────────────────────────────

const hex = (c, fallback) => {
  const m = /^#?([0-9a-f]{6})$/i.exec(String(c || ''));
  return m ? m[1].toUpperCase() : fallback;
};

const MARKER_ITEM = {
  type: 'object',
  properties: {
    query: str('Address or place name, used when longitude and latitude are not given.'),
    longitude: num('Longitude.'), latitude: num('Latitude.'),
    label: str('Short label, up to 3 characters on static maps.'),
    description: str('Text shown when the marker is selected.'),
    color: str('Color as six hex digits, for example D13438.')
  }
};
const LINE_ITEM = {
  type: 'object',
  properties: {
    name: str('Name for the legend.'),
    points: places('Points of the line in order.', 2),
    color: str('Color as six hex digits.'), width: int('Width in pixels, 1 to 20.'), dashed: bool('Draw dashed.')
  }
};
const POLYGON_ITEM = {
  type: 'object',
  properties: { name: str('Name for the legend.'), points: places('Corners of the polygon.', 3), color: str('Color as six hex digits.') }
};
const CIRCLE_ITEM = {
  type: 'object',
  properties: { name: str('Name for the legend.'), center: place('Center of the circle.'), radiusMeters: num('Radius in meters.'), color: str('Color as six hex digits.') }
};

async function resolveMarkers(items, ctx) {
  return Promise.all((items || []).map(async (m, i) => {
    const p = await ctx.places.resolve(m, `marker ${i + 1}`);
    // Pins show a short label (the number by default); the full name goes in the description.
    return { longitude: p.longitude, latitude: p.latitude, label: m.label || String(i + 1), description: m.description || m.name || p.name, color: `#${hex(m.color, PALETTE[i % PALETTE.length])}` };
  }));
}

async function resolveLayers(args, ctx) {
  const layers = [];
  for (const [i, l] of (args.lines || []).entries()) {
    const pts = await ctx.places.resolveAll(l.points, `line ${i + 1} point`);
    layers.push({ id: `line${i}`, type: 'line', name: l.name || `Line ${i + 1}`, coordinates: pts.map((p) => [p.longitude, p.latitude]), color: `#${hex(l.color, PALETTE[i % PALETTE.length])}`, width: l.width || 4, dashed: !!l.dashed });
  }
  for (const [i, g] of (args.polygons || []).entries()) {
    const pts = await ctx.places.resolveAll(g.points, `polygon ${i + 1} point`);
    const ring = pts.map((p) => [p.longitude, p.latitude]);
    layers.push({ id: `poly${i}`, type: 'polygon', name: g.name || `Area ${i + 1}`, geometry: { type: 'Polygon', coordinates: [[...ring, ring[0]]] }, color: `#${hex(g.color, PALETTE[(i + 2) % PALETTE.length])}`, fillOpacity: 0.2 });
  }
  for (const [i, c] of (args.circles || []).entries()) {
    const center = await ctx.places.resolve(c.center, `circle ${i + 1} center`);
    if (!c.radiusMeters) throw new InputError(`circle ${i + 1} needs radiusMeters.`);
    layers.push({ id: `circle${i}`, type: 'circle', name: c.name || `Circle ${i + 1}`, longitude: center.longitude, latitude: center.latitude, radiusMeters: c.radiusMeters, color: `#${hex(c.color, PALETTE[(i + 4) % PALETTE.length])}`, fillOpacity: 0.15 });
  }
  return layers;
}

function layerPositions(layers) {
  const out = [];
  for (const l of layers) {
    if (l.type === 'line') out.push(...l.coordinates);
    else if (l.type === 'polygon') out.push(...flatten(l.geometry.coordinates));
    else if (l.type === 'circle') {
      const dLat = l.radiusMeters / 111320, dLon = l.radiusMeters / (111320 * Math.max(0.01, Math.cos((l.latitude * Math.PI) / 180)));
      out.push([l.longitude - dLon, l.latitude - dLat], [l.longitude + dLon, l.latitude + dLat]);
    }
  }
  return out;
}

const MAP_PROPERTIES = {
  title: str('A title for the map.'),
  center: place('Center of the map. Not needed when markers or shapes define the view.'),
  zoom: int('Zoom level 1 (world) to 19 (street). Default: fit everything shown.', { minimum: 1, maximum: 19 }),
  bbox: str('Area to show as minLongitude,minLatitude,maxLongitude,maxLatitude.'),
  markers: arrayOf('Pins to show.', MARKER_ITEM),
  lines: arrayOf('Lines or routes to draw.', LINE_ITEM),
  polygons: arrayOf('Areas to outline and shade.', POLYGON_ITEM),
  circles: arrayOf('Circles to draw.', CIRCLE_ITEM),
  style: enumOf('Map style. Default road.', MAP_STYLES),
  showTraffic: bool('Show live traffic on the roads.'),
  language: str('Language for map labels, for example en-US.'),
  view: VIEWS
};

async function mapState(args, ctx) {
  const markers = await resolveMarkers(args.markers, ctx);
  const layers = await resolveLayers(args, ctx);
  const center = args.center ? await ctx.places.resolve(args.center, 'center') : undefined;
  let bbox;
  if (args.bbox) {
    const b = Array.isArray(args.bbox) ? args.bbox.map(Number) : String(args.bbox).split(',').map((s) => Number(s.trim()));
    if (b.length !== 4 || b.some((n) => !Number.isFinite(n))) throw new InputError('bbox must be minLongitude,minLatitude,maxLongitude,maxLatitude.');
    bbox = b;
  } else {
    const all = [...markers.map((m) => [m.longitude, m.latitude]), ...layerPositions(layers)];
    if (!args.zoom && all.length) bbox = bboxOf(all);
  }
  if (!center && !bbox && !markers.length && !layers.length) throw new InputError('Give a center, a bbox, markers or shapes so there is something to show.');
  return { markers, layers, center, bbox };
}

const tools = [];

tools.push(defineTool({
  name: 'show_map',
  title: 'Show an interactive map',
  description: 'Show an interactive map the user can pan and zoom, with pins, lines, areas and circles you provide, live traffic, and a choice of road, dark, satellite, hybrid and terrain styles. Use it to put any set of places, routes or areas on a map. The user can also switch on a weather radar layer. Pins and shapes accept addresses or coordinates.',
  properties: { ...MAP_PROPERTIES, radar: enumOf('Weather layer to show: radar or infrared. Default none.', ['none', 'radar', 'infrared']) },
  widget: 'map',
  async handler(args, ctx) {
    const { markers, layers, center, bbox } = await mapState(args, ctx);
    const text = `Interactive map${args.title ? `: ${args.title}` : ''} with ${markers.length} pin(s) and ${layers.length} shape(s).${markers.length ? `\n${markers.map((m, i) => `${i + 1}. ${m.label}${m.description && m.description !== m.label ? ` — ${m.description}` : ''} [${m.longitude}, ${m.latitude}]`).join('\n')}` : ''}\nThe map is shown when the client supports it. If the user cannot see it, describe the places in text.`;
    return ok(text, {
      kind: 'map', title: args.title || 'Map', style: args.style || 'road', traffic: !!args.showTraffic, radar: args.radar && args.radar !== 'none' ? args.radar : undefined,
      zoom: args.zoom, center: center ? { longitude: center.longitude, latitude: center.latitude } : undefined, bbox, markers, layers, language: args.language, view: args.view
    });
  }
}));

tools.push(defineTool({
  name: 'show_weather_radar',
  title: 'Show the weather radar',
  description: 'Show an animated weather radar (or infrared satellite) map for an area: the last 90 minutes and the forecast for the next 2 hours, which the user can play, pause and scrub.',
  properties: {
    center: place('The center of the map. Accepts an address, place name or coordinates.'),
    zoom: int('Zoom level 3 to 12. Default 6.', { minimum: 1, maximum: 12 }),
    layer: enumOf('radar (default) or infrared.', ['radar', 'infrared']),
    style: enumOf('Base map style. Default dark.', MAP_STYLES)
  },
  required: ['center'],
  widget: 'map',
  async handler(args, ctx) {
    const c = await ctx.places.resolve(args.center, 'center');
    return ok(`Weather ${args.layer === 'infrared' ? 'infrared satellite' : 'radar'} centered on ${labelOf(c)}. The animated map is shown when the client supports it.`, {
      kind: 'map', title: `Weather ${args.layer === 'infrared' ? 'infrared' : 'radar'}: ${labelOf(c)}`, style: args.style || 'dark', radar: args.layer === 'infrared' ? 'infrared' : 'radar', animate: true,
      zoom: args.zoom || 6, center: { longitude: c.longitude, latitude: c.latitude }, markers: [{ longitude: c.longitude, latitude: c.latitude, label: labelOf(c), color: '#C50F1F' }], layers: []
    });
  }
}));

// ───────────────────────────── static images ─────────────────────────────

const MAX_IMAGE_BYTES = 400 * 1024;

const pin = (m, i) => {
  const label = String(m.label || i + 1).replace(/[|']/g, '').slice(0, 3);
  return `default|co${hex(m.color, PALETTE[i % PALETTE.length])}||'${label}'${round(m.longitude, 6)} ${round(m.latitude, 6)}`;
};

const { polygons: _polygons, ...STATIC_MAP_PROPERTIES } = MAP_PROPERTIES;

tools.push(defineTool({
  name: 'get_static_map_image',
  title: 'Get a map image',
  description: 'Get a map as a PNG or JPEG image, with optional pins, lines, circles and live traffic. Use it when an image is needed rather than an interactive map, such as for a report. Give a center and zoom, a bbox, or pins that define the area.',
  properties: {
    ...STATIC_MAP_PROPERTIES,
    width: int('Image width in pixels, 80 to 2000. Default 800.', { minimum: 80, maximum: 2000 }),
    height: int('Image height in pixels, 80 to 1500. Default 500.', { minimum: 80, maximum: 1500 }),
    format: enumOf('Image format. Default jpeg, which is much smaller.', ['jpeg', 'png'])
  },
  async handler(args, ctx) {
    const { markers, layers, center, bbox } = await mapState({ ...args, polygons: undefined }, ctx);
    let width = args.width || 800, height = args.height || 500;
    const query = {};
    const style = { road: 'microsoft.base.road', dark: 'microsoft.base.darkgrey', satellite: 'microsoft.imagery', hybrid: 'microsoft.imagery', terrain: 'microsoft.base.road' }[args.style || 'road'];
    query.tilesetId = style;
    if (args.showTraffic) query.trafficLayer = 'microsoft.traffic.relative.main';
    if (args.language) query.language = args.language;
    if (args.view) query.view = args.view;
    if (bbox && !args.zoom && !args.center) {
      query.bbox = bbox.map((n) => round(n, 5));
      width = undefined; height = undefined;
    } else {
      const c = center || (bbox ? { longitude: (bbox[0] + bbox[2]) / 2, latitude: (bbox[1] + bbox[3]) / 2 } : null);
      if (!c) throw new InputError('Give a center and zoom, a bbox, or pins.');
      query.center = [round(c.longitude, 6), round(c.latitude, 6)];
      query.zoom = args.zoom || 12;
    }
    const pins = markers.map(pin);
    const paths = [];
    for (const l of layers) {
      if (l.type === 'line') paths.push(`lc${hex(l.color, '0078D4')}|lw${Math.min(20, l.width || 4)}||${simplifyLine(l.coordinates, 90).map(([lo, la]) => `${round(lo, 5)} ${round(la, 5)}`).join('|')}`);
      else if (l.type === 'circle') paths.push(`lc${hex(l.color, '0078D4')}|lw2|ra${Math.round(l.radiusMeters)}||${round(l.longitude, 6)} ${round(l.latitude, 6)}`);
    }

    let jpeg = args.format !== 'png';
    let image;
    for (let attempt = 0; attempt < 5; attempt++) {
      image = await ctx.maps.binary('/map/static', {
        version: VERSIONS.render, accept: jpeg ? 'image/jpeg' : 'image/png',
        query: { ...query, width, height, pins, path: paths }
      });
      if (image.bytes.length <= MAX_IMAGE_BYTES) break;
      if (!jpeg) jpeg = true;
      else if (width && height) { width = Math.max(80, Math.round(width * 0.75)); height = Math.max(80, Math.round(height * 0.75)); }
      else break;
    }
    return ok(`Map image (${image.mimeType}, ${Math.round(image.bytes.length / 1024)} KB)${args.title ? `: ${args.title}` : ''}.`, undefined, [imageContent(image)]);
  }
}));

// ───────────────────────────── tiles, tilesets, attribution, copyright ─────────────────────────────

tools.push(defineTool({
  name: 'get_map_tile',
  title: 'Get a map tile',
  description: 'Get one raster map tile as an image: road, dark, satellite, terrain, weather radar, infrared, or a traffic overlay. Tiles are 256 or 512 pixels square on the standard web map grid (zoom, x, y). Interactive maps call this to draw themselves.',
  properties: {
    tilesetId: enumOf('Tileset. Default microsoft.base.road.', TILESETS),
    zoom: int('Zoom level, 0 to 22.', { minimum: 0, maximum: 22 }),
    x: int('Tile column, 0 to 2^zoom - 1.', { minimum: 0 }),
    y: int('Tile row, 0 to 2^zoom - 1.', { minimum: 0 }),
    tileSize: enumOf('Tile size in pixels: 256 (default) or 512.', ['256', '512']),
    timeStamp: str('For weather radar (up to 1.5 hours back and 2 hours ahead) and infrared (up to 3 hours back): the time of the tile, ISO 8601.'),
    language: str('Language for labels, for example en-US.'),
    view: VIEWS
  },
  required: ['zoom', 'x', 'y'],
  async handler(args, ctx) {
    const tilesetId = oneOf(args.tilesetId, 'tilesetId', TILESETS, 'microsoft.base.road');
    const zoom = intInRange(args.zoom, 'zoom', 0, 22);
    const limit = 2 ** zoom - 1;
    const x = intInRange(args.x, 'x', 0, limit), y = intInRange(args.y, 'y', 0, limit);
    const tile = await fetchTile(ctx, { tilesetId, zoom, x, y, tileSize: Number(args.tileSize) === 512 ? 512 : 256, timeStamp: args.timeStamp, language: args.language, view: args.view });
    return { content: [imageContent(tile), { type: 'text', text: `${tilesetId} tile ${zoom}/${x}/${y}` }], isError: false };
  }
}));

tools.push(defineTool({
  name: 'get_traffic_tile',
  title: 'Get a traffic flow or incident tile',
  description: 'Get a raster traffic tile from the Traffic service: flow (colored roads) or incident icons, in several styles.',
  properties: {
    kind: enumOf('flow (default) or incident.', ['flow', 'incident']),
    style: str('For flow: absolute, relative, relative-delay or reduced-sensitivity. For incident: s1, s2, s3 or night. Default absolute for flow and s3 for incident.'),
    zoom: int('Zoom level, 0 to 22.', { minimum: 0, maximum: 22 }),
    x: int('Tile column.', { minimum: 0 }), y: int('Tile row.', { minimum: 0 }),
    thickness: int('Line thickness multiplier for flow tiles, 1 to 20.')
  },
  required: ['zoom', 'x', 'y'],
  async handler(args, ctx) {
    const kind = args.kind === 'incident' ? 'incident' : 'flow';
    const zoom = intInRange(args.zoom, 'zoom', 0, 22);
    const tile = await ctx.maps.binary(`/traffic/${kind}/tile/png`, {
      version: VERSIONS.trafficV1, accept: 'image/png',
      query: { style: args.style || (kind === 'flow' ? 'absolute' : 's3'), zoom, x: args.x, y: args.y, thickness: kind === 'flow' ? args.thickness : undefined }
    });
    return { content: [imageContent(tile), { type: 'text', text: `Traffic ${kind} tile ${zoom}/${args.x}/${args.y}` }], isError: false };
  }
}));

tools.push(defineTool({
  name: 'get_map_tileset',
  title: 'Get tileset information',
  description: 'Get metadata for a map tileset: its zoom range, bounds, attribution and tile URL pattern.',
  properties: { tilesetId: enumOf('The tileset.', TILESETS) },
  required: ['tilesetId'],
  async handler(args, ctx) {
    const data = await ctx.maps.json('/map/tileset', { version: VERSIONS.render, query: { tilesetId: oneOf(args.tilesetId, 'tilesetId', TILESETS) } });
    return dataResult(`Tileset ${args.tilesetId}.`, data);
  }
}));

tools.push(defineTool({
  name: 'get_map_attribution',
  title: 'Get map attribution text',
  description: 'Get the copyright attribution text that must be shown with a map of an area at a zoom level.',
  properties: {
    tilesetId: enumOf('The tileset the map uses. Default microsoft.base.road.', TILESETS), zoom: int('Zoom level.', { minimum: 0, maximum: 22 }),
    bounds: str('The area as minLongitude,minLatitude,maxLongitude,maxLatitude.')
  },
  required: ['zoom', 'bounds'],
  async handler(args, ctx) {
    const b = String(args.bounds).split(',').map((s) => Number(s.trim()));
    if (b.length !== 4 || b.some((n) => !Number.isFinite(n))) throw new InputError('bounds must be minLongitude,minLatitude,maxLongitude,maxLatitude.');
    const data = await ctx.maps.json('/map/attribution', { version: VERSIONS.render, query: { tilesetId: oneOf(args.tilesetId, 'tilesetId', TILESETS, 'microsoft.base.road'), zoom: args.zoom, bounds: b } });
    return dataResult(`Attribution: ${(data.copyrights || []).join(' ')}`, data);
  }
}));

tools.push(defineTool({
  name: 'get_copyright',
  title: 'Get map copyright information',
  description: 'Get copyright information for the world, a bounding box or one tile, or the copyright caption that goes with a rendered tile.',
  properties: {
    scope: enumOf('world (default), bbox, tile or caption.', ['world', 'bbox', 'tile', 'caption']),
    bbox: str('For scope bbox: minLongitude,minLatitude,maxLongitude,maxLatitude.'),
    zoom: int('For scope tile: zoom level.'), x: int('For scope tile: tile column.'), y: int('For scope tile: tile row.'),
    includeText: bool('Include the full legal text. Default false, which returns only country names and images.')
  },
  async handler(args, ctx) {
    const scope = oneOf(args.scope, 'scope', ['world', 'bbox', 'tile', 'caption'], 'world');
    const text = args.includeText === true ? 'yes' : 'no';
    let data;
    if (scope === 'world') data = await ctx.maps.json('/map/copyright/world/json', { version: VERSIONS.render, query: { text } });
    else if (scope === 'caption') data = await ctx.maps.json('/map/copyright/caption/json', { version: VERSIONS.render });
    else if (scope === 'tile') {
      for (const k of ['zoom', 'x', 'y']) if (args[k] === undefined) throw new InputError(`${k} is required for scope tile.`);
      data = await ctx.maps.json('/map/copyright/tile/json', { version: VERSIONS.render, query: { zoom: args.zoom, x: args.x, y: args.y, text } });
    } else {
      const b = String(args.bbox || '').split(',').map((s) => Number(s.trim()));
      if (b.length !== 4 || b.some((n) => !Number.isFinite(n))) throw new InputError('bbox must be minLongitude,minLatitude,maxLongitude,maxLatitude.');
      data = await ctx.maps.json('/map/copyright/bounding/json', { version: VERSIONS.render, query: { mincoordinates: [b[1], b[0]], maxcoordinates: [b[3], b[2]], text } });
    }
    return dataResult(`Copyright information (${scope}).`, data);
  }
}));

module.exports = { tools, fetchTile, TILESETS };
