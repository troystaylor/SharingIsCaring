'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult } = require('../define');
const { str, num, int, bool, enumOf, arrayOf, strings, place, places } = require('../schema');
const { InputError, intInRange, oneOf, list, parsePlace, labelOf, requireNumber } = require('../args');
const { flatten, bboxOf, simplifyLine, roundLine, round, geometryAreaSqm, simplifyGeometry } = require('../geo');

const GUIDANCE = 'A map and list of these places are shown when the client supports it. If the user cannot see them, summarize the places in text.';
const COUNTRY_NOTE = 'ISO 3166-1 alpha-2 country codes such as US or FR';

// ─────────────────────────── normalizers ───────────────────────────

/** A GeoJSON feature from the Geocoding, Reverse Geocoding and Autocomplete APIs (version 2026-01-01). */
function fromFeature(feature) {
  const props = feature.properties || {};
  const a = props.address || {};
  const coords = feature.geometry && feature.geometry.coordinates;
  const region = (a.adminDistricts || [])[0] || {};
  const formatted = a.formattedAddress || '';
  const isAddress = !props.type || props.type === 'Address';
  const placeName = isAddress ? (a.addressLine || formatted) : formatted.split(',')[0];
  const p = {
    name: placeName || formatted || 'Location',
    address: formatted || undefined,
    type: props.type,
    ...(props.confidence ? { confidence: props.confidence } : {}),
    ...(props.matchCodes ? { matchCodes: props.matchCodes } : {}),
    locality: a.locality,
    region: region.shortName || region.name,
    postalCode: a.postalCode,
    country: a.countryRegion && a.countryRegion.name,
    countryCode: a.countryRegion && a.countryRegion.ISO,
    ...(a.intersection && a.intersection.displayName ? { intersection: a.intersection.displayName } : {})
  };
  if (Array.isArray(coords) && coords.length >= 2) {
    p.longitude = round(coords[0], 6);
    p.latitude = round(coords[1], 6);
  }
  if (Array.isArray(feature.bbox) && feature.bbox.length === 4) p.bbox = feature.bbox.map((n) => round(n, 5));
  return Object.fromEntries(Object.entries(p).filter(([, v]) => v !== undefined && v !== ''));
}

const pad = (n) => String(n).padStart(2, '0');

/** A result from the Search 1.0 APIs. */
function fromV1(result) {
  const poi = result.poi || {};
  const a = result.address || {};
  const pos = result.position || {};
  const p = {
    id: result.id,
    type: result.type,
    name: poi.name || a.freeformAddress || (a.streetName ? `${a.streetNumber || ''} ${a.streetName}`.trim() : a.municipality) || 'Location',
    address: a.freeformAddress,
    longitude: pos.lon !== undefined ? round(pos.lon, 6) : undefined,
    latitude: pos.lat !== undefined ? round(pos.lat, 6) : undefined,
    category: (poi.categories || [])[0],
    categories: poi.categories && poi.categories.length > 1 ? poi.categories : undefined,
    phone: poi.phone,
    url: poi.url,
    brands: poi.brands ? poi.brands.map((b) => b.name) : undefined,
    distance: result.dist !== undefined ? Math.round(result.dist) : undefined,
    detourSeconds: result.detourTime,
    locality: a.municipality,
    region: a.countrySubdivision,
    postalCode: a.postalCode,
    country: a.country,
    countryCode: a.countryCode,
    entityType: result.entityType,
    geometryId: result.dataSources && result.dataSources.geometry && result.dataSources.geometry.id,
    score: result.score !== undefined ? round(result.score, 3) : undefined
  };
  if (result.viewport && result.viewport.topLeftPoint && result.viewport.btmRightPoint) {
    const tl = result.viewport.topLeftPoint, br = result.viewport.btmRightPoint;
    p.bbox = [tl.lon, br.lat, br.lon, tl.lat].map((n) => round(n, 5));
  }
  if (poi.openingHours && Array.isArray(poi.openingHours.timeRanges)) {
    p.hours = poi.openingHours.timeRanges.map((r) => ({
      date: r.startTime.date,
      open: `${pad(r.startTime.hour)}:${pad(r.startTime.minute)}`,
      close: `${pad(r.endTime.hour)}:${pad(r.endTime.minute)}`
    }));
  }
  return Object.fromEntries(Object.entries(p).filter(([, v]) => v !== undefined && v !== ''));
}

function placeLine(p, i) {
  const parts = [`${i + 1}. ${p.name}`];
  if (p.address && p.address !== p.name) parts.push(`— ${p.address}`);
  if (p.longitude !== undefined) parts.push(`[longitude ${p.longitude}, latitude ${p.latitude}]`);
  const bits = [];
  if (p.category || p.type) bits.push(p.category || p.type);
  if (p.confidence) bits.push(`${p.confidence} confidence`);
  if (p.distance !== undefined) bits.push(`${p.distance} m away`);
  if (p.detourSeconds !== undefined) bits.push(`${Math.round(p.detourSeconds / 60)} min detour`);
  if (p.phone) bits.push(p.phone);
  if (p.url) bits.push(p.url);
  if (bits.length) parts.push(`(${bits.join(', ')})`);
  return parts.join(' ');
}

/**
 * Builds the result for any tool that returns places. The agent gets a numbered list. The places widget gets
 * structuredContent, including the search that produced it so it can repeat the search for another area.
 */
function placesResult({ lead, places: found, title, total, center, searchContext, route }) {
  const lines = found.map(placeLine).join('\n');
  const text = found.length
    ? `${lead}\n${lines}\n${GUIDANCE}`
    : `${lead}\nNo places were found. Try a broader query or a larger area.`;
  return ok(text, {
    kind: 'places',
    title,
    places: found,
    total: total ?? found.length,
    ...(center ? { center } : {}),
    ...(route ? { route } : {}),
    ...(searchContext ? { searchContext } : {})
  });
}

// ─────────────────────────── shared parameters ───────────────────────────

const LANGUAGE = str('Language for results as an IETF tag, for example en-US.');
const VIEW = str('Region view for disputed borders, an ISO 3166-1 alpha-2 code such as US, or Auto.');
const COUNTRY_SET = strings(`Limit results to these countries. ${COUNTRY_NOTE}.`);
const CATEGORY_SET = arrayOf('Limit results to these point-of-interest category IDs. Use get_poi_categories to find IDs.', { type: 'integer' });
const BRAND_SET = strings('Limit results to these brand names, for example Starbucks.');
const OPENING_HOURS = enumOf('Set to nextSevenDays to include opening hours where available.', ['nextSevenDays']);
const CONNECTORS = strings('For electric vehicle stations, limit to these connector types, for example IEC62196Type2CCS or Chademo.');
const NEAR = place('Where to bias results, such as the user\'s location or a city.');
const BBOX = str('Limit to an area as minLongitude,minLatitude,maxLongitude,maxLatitude.');
const LIMIT = int('Maximum results, 1 to 100. Default 10.', { minimum: 1, maximum: 100 });
const OFFSET = int('Starting offset for paging. Default 0.', { minimum: 0 });
const RADIUS = int('Limit results to this radius in meters around the bias point.', { minimum: 1 });
const EXTENDED_POSTAL = strings('Include extended postal codes for these indexes: Addr, Geo, PAD, POI, Str, Xstr.');

function parseBbox(value) {
  if (value === undefined) return undefined;
  const parts = Array.isArray(value) ? value.map(Number) : String(value).split(',').map((s) => Number(s.trim()));
  if (parts.length !== 4 || parts.some((n) => !Number.isFinite(n))) throw new InputError('bbox must be minLongitude,minLatitude,maxLongitude,maxLatitude.');
  return parts;
}

/** Search 1.0 query parameters shared by the fuzzy, POI, category, nearby and address searches. */
async function v1Params(args, ctx, { withNear = true } = {}) {
  const q = {
    limit: intInRange(args.limit, 'limit', 1, 100, 10),
    ofs: args.ofs,
    countrySet: list(args.countrySet).join(',') || undefined,
    categorySet: list(args.categorySet).join(',') || undefined,
    brandSet: list(args.brandSet).join(',') || undefined,
    connectorSet: list(args.connectorSet).join(',') || undefined,
    language: args.language,
    view: args.view,
    typeahead: args.typeahead,
    radius: args.radius,
    openingHours: args.openingHours,
    entityType: args.entityType,
    idxSet: list(args.idxSet).join(',') || undefined,
    minFuzzyLevel: args.minFuzzyLevel,
    maxFuzzyLevel: args.maxFuzzyLevel,
    extendedPostalCodesFor: list(args.extendedPostalCodesFor).join(',') || undefined
  };
  let center;
  if (withNear && args.near) {
    const near = await ctx.places.resolve(args.near, 'near');
    q.lat = near.latitude;
    q.lon = near.longitude;
    center = { longitude: near.longitude, latitude: near.latitude, name: near.name };
  }
  const bbox = parseBbox(args.bbox);
  if (bbox) {
    q.topLeft = `${bbox[3]},${bbox[0]}`;
    q.btmRight = `${bbox[1]},${bbox[2]}`;
  }
  return { query: q, center };
}

const V1_FILTERS = {
  near: NEAR, radius: RADIUS, bbox: BBOX, countrySet: COUNTRY_SET, categorySet: CATEGORY_SET, brandSet: BRAND_SET,
  limit: LIMIT, ofs: OFFSET, openingHours: OPENING_HOURS, connectorSet: CONNECTORS, language: LANGUAGE, view: VIEW,
  extendedPostalCodesFor: EXTENDED_POSTAL
};

async function v1Search(kind, args, ctx, extraQuery = {}) {
  const { query, center } = await v1Params(args, ctx);
  const data = await ctx.maps.json(`/search/${kind}/json`, {
    version: VERSIONS.searchV1,
    query: { ...query, ...extraQuery }
  });
  const found = (data.results || []).map(fromV1);
  return { found, center, summary: data.summary || {} };
}

// ─────────────────────────── tools ───────────────────────────

const tools = [];

tools.push(defineTool({
  name: 'geocode',
  title: 'Geocode an address or place',
  description: 'Turn an address, landmark or place name into coordinates (forward geocoding). Use query for free text such as "1 Microsoft Way, Redmond, WA", or the structured fields (addressLine, locality, adminDistrict, postalCode, countryRegion) instead, not both. Each result has a confidence level and match codes: check them, because a low-confidence match can be a nearby or larger area. Shows the matches on a map.',
  properties: {
    query: str('Free-text address or place name. Do not combine with the structured address fields.'),
    addressLine: str('Street line of a structured address, for example "400 Broad St".'),
    locality: str('City or town of a structured address.'),
    adminDistrict: str('State or province of a structured address, for example WA.'),
    adminDistrict2: str('County of a structured address.'),
    adminDistrict3: str('Named area of a structured address.'),
    postalCode: str('Postal code of a structured address.'),
    countryRegion: str(`Country or region to favor. ${COUNTRY_NOTE}.`),
    near: NEAR,
    bbox: BBOX,
    top: int('Maximum results, 1 to 20. Default 5.', { minimum: 1, maximum: 20 }),
    view: VIEW,
    language: LANGUAGE
  },
  widget: 'places',
  async handler(args, ctx) {
    const structured = ['addressLine', 'locality', 'adminDistrict', 'adminDistrict2', 'adminDistrict3', 'postalCode'].some((k) => args[k]);
    if (!args.query && !structured) throw new InputError('Provide query, or at least one structured address field.');
    if (args.query && structured) throw new InputError('Use either query or the structured address fields, not both.');
    const near = args.near ? await ctx.places.resolve(args.near, 'near') : undefined;
    const bbox = parseBbox(args.bbox);
    const data = await ctx.maps.json('/geocode', {
      version: VERSIONS.search,
      accept: 'application/geo+json',
      language: args.language,
      query: {
        query: args.query, addressLine: args.addressLine, locality: args.locality, adminDistrict: args.adminDistrict,
        adminDistrict2: args.adminDistrict2, adminDistrict3: args.adminDistrict3, postalCode: args.postalCode,
        countryRegion: args.countryRegion, top: intInRange(args.top, 'top', 1, 20, 5), view: args.view,
        coordinates: near ? [near.longitude, near.latitude] : undefined, bbox
      }
    });
    const found = (data.features || []).map(fromFeature);
    return placesResult({ lead: `Found ${found.length} match(es) for "${args.query || args.addressLine}".`, places: found, title: `Results for ${args.query || args.addressLine}`, searchContext: { tool: 'geocode', args: { query: args.query } } });
  }
}));

tools.push(defineTool({
  name: 'autocomplete_places',
  title: 'Autocomplete a place or address',
  description: 'Suggest addresses and places as the user types, ranked by closeness to a location. Give a partial query such as "1 Micro" and either near or bbox to bias results. Suggestions have no coordinates: geocode the one the user picks.',
  properties: {
    query: str('The partial text typed so far, for example "1 Micro" or "Muir Woods".'),
    near: NEAR,
    bbox: BBOX,
    top: int('Maximum suggestions, 1 to 20. Default 5.', { minimum: 1, maximum: 20 }),
    resultTypeGroups: strings('Limit to these groups: Address, Place.'),
    resultTypes: strings('Limit to these entity types, for example Address, PopulatedPlace, Park, Museum.'),
    countryRegion: str(`Country or region to favor. ${COUNTRY_NOTE}.`),
    view: VIEW,
    language: LANGUAGE
  },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    const near = args.near ? await ctx.places.resolve(args.near, 'near') : undefined;
    const bbox = parseBbox(args.bbox);
    if (!near && !bbox) throw new InputError('Provide near (a place, or longitude and latitude) or bbox so suggestions can be ranked.');
    const data = await ctx.maps.json('/geocode:autocomplete', {
      version: VERSIONS.search,
      accept: 'application/geo+json',
      language: args.language,
      query: {
        query: args.query, coordinates: near ? [near.longitude, near.latitude] : undefined, bbox, top: intInRange(args.top, 'top', 1, 20, 5),
        resultTypeGroups: list(args.resultTypeGroups), resultTypes: list(args.resultTypes), countryRegion: args.countryRegion, view: args.view
      }
    });
    const suggestions = (data.features || []).map((f) => ({ ...fromFeature(f), suggestion: true }));
    const lines = suggestions.map((s, i) => `${i + 1}. ${s.address || s.name}${s.type ? ` (${s.type})` : ''}`).join('\n');
    return ok(`${suggestions.length} suggestion(s) for "${args.query}":\n${lines}\nSuggestions have no coordinates. Call geocode with the chosen address to get its location.`, {
      kind: 'places', title: `Suggestions for ${args.query}`, places: suggestions, suggestions: true, ...(near ? { center: { longitude: near.longitude, latitude: near.latitude } } : {}),
      searchContext: { tool: 'autocomplete_places', args: { query: args.query } }
    });
  }
}));

tools.push(defineTool({
  name: 'reverse_geocode',
  title: 'Find the address at a location',
  description: 'Turn coordinates into the street address, neighborhood, city, region and country at that point (reverse geocoding). Shows the result on a map.',
  properties: {
    location: place('The point to look up. Coordinates are expected here; text is geocoded first.'),
    resultTypes: strings('Limit to these entity types: Address, Neighborhood, PopulatedPlace, Postcode1, AdminDivision1, AdminDivision2, CountryRegion.'),
    view: VIEW,
    language: LANGUAGE
  },
  required: ['location'],
  widget: 'places',
  async handler(args, ctx) {
    const at = await ctx.places.resolve(args.location, 'location');
    const data = await ctx.maps.json('/reverseGeocode', {
      version: VERSIONS.search,
      accept: 'application/geo+json',
      language: args.language,
      query: { coordinates: [at.longitude, at.latitude], resultTypes: list(args.resultTypes), view: args.view }
    });
    const found = (data.features || []).map(fromFeature);
    found.forEach((p) => { p.longitude ??= at.longitude; p.latitude ??= at.latitude; });
    return placesResult({
      lead: found.length ? `Address at ${at.latitude}, ${at.longitude}: ${found[0].address || found[0].name}.` : `No address was found at ${at.latitude}, ${at.longitude}.`,
      places: found, title: 'Address at this location', center: { longitude: at.longitude, latitude: at.latitude },
      searchContext: { tool: 'reverse_geocode', args: {} }
    });
  }
}));

const BATCH_NOTE = 'Up to 100 items per call.';

tools.push(defineTool({
  name: 'batch_geocode',
  title: 'Geocode many addresses',
  description: `Geocode a list of addresses in one request and see which ones matched well. ${BATCH_NOTE} Shows a table with a confidence summary, a map of the matches, and a CSV download. Items are address text, or objects with the structured fields.`,
  properties: {
    addresses: arrayOf(`Addresses to geocode. ${BATCH_NOTE}`, { type: 'string' }),
    items: arrayOf('Alternative to addresses: objects with query or the structured fields (addressLine, locality, adminDistrict, postalCode, countryRegion), each with an optional top.', {
      type: 'object',
      properties: {
        query: str('Free-text address.'), addressLine: str('Street line.'), locality: str('City.'), adminDistrict: str('State or province.'),
        postalCode: str('Postal code.'), countryRegion: str('Country code.'), top: int('Maximum results for this item, 1 to 20.')
      }
    }),
    view: VIEW,
    language: LANGUAGE
  },
  widget: 'batch',
  async handler(args, ctx) {
    const inputs = [...(args.addresses || []).map((a) => ({ query: String(a) })), ...(args.items || [])];
    if (!inputs.length) throw new InputError('Provide addresses or items.');
    if (inputs.length > 100) throw new InputError('A batch can hold at most 100 items.');
    const data = await ctx.maps.batch(inputs, (it) => ctx.maps.json('/geocode', {
      version: VERSIONS.search, accept: 'application/geo+json', language: args.language,
      query: { ...it, top: it.top || 1, view: args.view }
    }));
    const rows = (data.batchItems || []).map((item, i) => {
      const first = (item.features || [])[0];
      const input = inputs[i].query || [inputs[i].addressLine, inputs[i].locality, inputs[i].adminDistrict, inputs[i].postalCode].filter(Boolean).join(', ');
      if (!first) return { index: i + 1, input, matched: false, error: item.error && item.error.message };
      return { index: i + 1, input, matched: true, ...fromFeature(first) };
    });
    const summary = { total: rows.length, matched: rows.filter((r) => r.matched).length, high: rows.filter((r) => r.confidence === 'High').length, medium: rows.filter((r) => r.confidence === 'Medium').length, low: rows.filter((r) => r.matched && r.confidence === 'Low').length };
    const lines = rows.map((r) => (r.matched ? `${r.index}. "${r.input}" → ${r.address || r.name} [${r.longitude}, ${r.latitude}] ${r.confidence || ''}`.trim() : `${r.index}. "${r.input}" → no match${r.error ? ` (${r.error})` : ''}`)).join('\n');
    return ok(`Geocoded ${summary.total} item(s): ${summary.matched} matched (${summary.high} high, ${summary.medium} medium, ${summary.low} low confidence).\n${lines}`, { kind: 'batch', mode: 'geocode', rows, summary });
  }
}));

tools.push(defineTool({
  name: 'batch_reverse_geocode',
  title: 'Find addresses for many points',
  description: `Reverse geocode a list of coordinates in one request. ${BATCH_NOTE} Shows a table of addresses and a map of the points.`,
  properties: {
    points: places(`Points to look up as coordinates. ${BATCH_NOTE}`, 1, 100),
    resultTypes: strings('Limit to these entity types: Address, Neighborhood, PopulatedPlace, Postcode1, AdminDivision1, AdminDivision2, CountryRegion.'),
    view: VIEW,
    language: LANGUAGE
  },
  required: ['points'],
  widget: 'batch',
  async handler(args, ctx) {
    const pts = await ctx.places.resolveAll(args.points, 'point');
    if (pts.length > 100) throw new InputError('A batch can hold at most 100 items.');
    const data = await ctx.maps.batch(pts, (p) => ctx.maps.json('/reverseGeocode', {
      version: VERSIONS.search, accept: 'application/geo+json', language: args.language,
      query: { coordinates: [p.longitude, p.latitude], resultTypes: list(args.resultTypes), view: args.view }
    }));
    const rows = (data.batchItems || []).map((item, i) => {
      const first = (item.features || [])[0];
      const input = `${pts[i].latitude.toFixed(5)}, ${pts[i].longitude.toFixed(5)}`;
      if (!first) return { index: i + 1, input, matched: false, longitude: pts[i].longitude, latitude: pts[i].latitude, error: item.error && item.error.message };
      const p = fromFeature(first);
      return { index: i + 1, input, matched: true, ...p, longitude: pts[i].longitude, latitude: pts[i].latitude };
    });
    const summary = { total: rows.length, matched: rows.filter((r) => r.matched).length };
    const lines = rows.map((r) => `${r.index}. ${r.input} → ${r.matched ? r.address || r.name : 'no address'}`).join('\n');
    return ok(`Reverse geocoded ${summary.total} point(s): ${summary.matched} had an address.\n${lines}`, { kind: 'batch', mode: 'reverse', rows, summary });
  }
}));

const BOUNDARY_TYPES = ['countryRegion', 'adminDistrict', 'adminDistrict2', 'postalCode', 'postalCode2', 'postalCode3', 'postalCode4', 'neighborhood', 'locality'];

tools.push(defineTool({
  name: 'get_boundary',
  title: 'Get an administrative boundary',
  description: 'Get the boundary polygon of the city, postal code, county, state, neighborhood or country that contains a point. Returns the area, the bounding box and a simplified GeoJSON polygon, and draws the boundary on a map.',
  properties: {
    location: place('A point inside the area whose boundary you want.'),
    resultType: enumOf('Which boundary to return. Default countryRegion.', BOUNDARY_TYPES),
    resolution: enumOf('Level of detail. Default medium.', ['small', 'medium', 'large', 'huge']),
    view: VIEW
  },
  required: ['location'],
  widget: 'map',
  async handler(args, ctx) {
    const at = await ctx.places.resolve(args.location, 'location');
    const type = oneOf(args.resultType, 'resultType', BOUNDARY_TYPES, 'locality');
    const data = await ctx.maps.json('/search/polygon', {
      version: VERSIONS.search, accept: 'application/geo+json',
      query: { coordinates: [at.longitude, at.latitude], resultType: type, resolution: oneOf(args.resolution, 'resolution', ['small', 'medium', 'large', 'huge'], 'small'), view: args.view }
    });
    const geometry = simplifyGeometry(data.geometry, 300);
    const all = flatten(geometry.coordinates);
    const bbox = bboxOf(all);
    const areaSqKm = round(geometryAreaSqm(data.geometry) / 1e6, 2);
    const name = (data.properties && data.properties.name) || at.name || 'Boundary';
    const copyright = data.properties && data.properties.copyright;
    const brief = simplifyGeometry(data.geometry, 80);
    return ok(`Boundary of ${name} (${type}): about ${areaSqKm} square kilometers, bounding box ${bbox ? bbox.map((n) => round(n, 4)).join(', ') : 'unknown'} (minLon, minLat, maxLon, maxLat). A coarse outline of the polygon follows; the map shows more detail.\n${JSON.stringify(brief)}\n${GUIDANCE}`, {
      kind: 'map', title: `${name} boundary`,
      layers: [{ id: 'boundary', type: 'polygon', name, geometry, color: '#0078D4', fillOpacity: 0.18 }],
      markers: [{ longitude: at.longitude, latitude: at.latitude, label: at.name || 'Point', color: '#C50F1F' }],
      bbox, facts: [{ label: 'Type', value: type }, { label: 'Area', value: `${areaSqKm.toLocaleString('en-US')} km²` }], attribution: copyright
    });
  }
}));

tools.push(defineTool({
  name: 'search_fuzzy',
  title: 'Search for places, addresses and points of interest',
  description: 'Free-form search that handles addresses, place names, businesses and categories in one query, for example "pizza near Pike Place" or "Seattle". Use near to bias results toward a location. Shows the results on a map with a list the user can browse, and the user can search the visible area.',
  properties: {
    query: str('What to look for, for example "coffee", "Space Needle" or "1 Microsoft Way".'),
    typeahead: bool('Treat the query as partial input and predict the rest.'),
    idxSet: strings('Indexes to search: Addr, Geo, PAD, POI, Str, Xstr.'),
    entityType: enumOf('Limit geographic results to this level.', ['Country', 'CountrySubdivision', 'CountrySecondarySubdivision', 'CountryTertiarySubdivision', 'Municipality', 'MunicipalitySubdivision', 'Neighbourhood', 'PostalCodeArea']),
    minFuzzyLevel: int('Minimum fuzziness, 1 to 4.', { minimum: 1, maximum: 4 }),
    maxFuzzyLevel: int('Maximum fuzziness, 1 to 4.', { minimum: 1, maximum: 4 }),
    ...V1_FILTERS
  },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    const { found, center, summary } = await v1Search('fuzzy', args, ctx, { query: args.query });
    return placesResult({ lead: `Found ${summary.totalResults ?? found.length} result(s) for "${args.query}", showing ${found.length}.`, places: found, total: summary.totalResults, title: `Results for ${args.query}`, center, searchContext: { tool: 'search_fuzzy', args: { query: args.query, ...(args.categorySet ? { categorySet: args.categorySet } : {}) } } });
  }
}));

tools.push(defineTool({
  name: 'search_poi',
  title: 'Search points of interest by name',
  description: 'Find businesses and landmarks by name or type, such as "Starbucks" or "statue of liberty", near a location. Shows them on a map with a list, opening hours when requested, and a button to search the visible area.',
  properties: { query: str('The point of interest to look for, for example "starbucks".'), typeahead: bool('Treat the query as partial input.'), ...V1_FILTERS },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    const { found, center, summary } = await v1Search('poi', args, ctx, { query: args.query });
    return placesResult({ lead: `Found ${summary.totalResults ?? found.length} point(s) of interest for "${args.query}", showing ${found.length}.`, places: found, total: summary.totalResults, title: `${args.query} nearby`, center, searchContext: { tool: 'search_poi', args: { query: args.query } } });
  }
}));

tools.push(defineTool({
  name: 'search_poi_category',
  title: 'Search points of interest by category',
  description: 'Find places of one category near a location, for example RESTAURANT, PHARMACY, PETROL_STATION, ELECTRIC_VEHICLE_STATION or AIRPORT. Use get_poi_categories to look up category names and IDs. Shows the places on a map with a list.',
  properties: { query: str('The category name, for example "RESTAURANT" or "ELECTRIC_VEHICLE_STATION".'), typeahead: bool('Treat the query as partial input.'), ...V1_FILTERS },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    const { found, center, summary } = await v1Search('poi/category', args, ctx, { query: args.query });
    return placesResult({ lead: `Found ${summary.totalResults ?? found.length} ${args.query} place(s), showing ${found.length}.`, places: found, total: summary.totalResults, title: `${args.query}`, center, searchContext: { tool: 'search_poi_category', args: { query: args.query } } });
  }
}));

tools.push(defineTool({
  name: 'search_nearby',
  title: 'Find places near a point',
  description: 'List points of interest around a location, nearest first, optionally limited to categories or brands. Use this for "what is near here" questions. Shows them on a map with distances.',
  properties: {
    location: place('The center of the search.'),
    radius: int('Search radius in meters, 1 to 50000. Default 2000.', { minimum: 1, maximum: 50000 }),
    limit: LIMIT, ofs: OFFSET, categorySet: CATEGORY_SET, brandSet: BRAND_SET, countrySet: COUNTRY_SET, connectorSet: CONNECTORS,
    language: LANGUAGE, view: VIEW, extendedPostalCodesFor: EXTENDED_POSTAL
  },
  required: ['location'],
  widget: 'places',
  async handler(args, ctx) {
    const at = await ctx.places.resolve(args.location, 'location');
    const { query } = await v1Params({ ...args, near: undefined }, ctx);
    const data = await ctx.maps.json('/search/nearby/json', {
      version: VERSIONS.searchV1, query: { ...query, lat: at.latitude, lon: at.longitude, radius: args.radius || 2000 }
    });
    const found = (data.results || []).map(fromV1);
    return placesResult({
      lead: `Found ${found.length} place(s) within ${args.radius || 2000} m of ${labelOf(at, 'the point')}, nearest first.`, places: found, title: `Near ${labelOf(at, 'this point')}`,
      center: { longitude: at.longitude, latitude: at.latitude, name: at.name }, searchContext: { tool: 'search_nearby', args: { radius: args.radius || 2000, ...(args.categorySet ? { categorySet: args.categorySet } : {}) } }
    });
  }
}));

tools.push(defineTool({
  name: 'search_structured_address',
  title: 'Search a structured address',
  description: 'Find an address from its parts: street number, street name, city, state, postal code and a required country code. More exact than free text when you already have the parts.',
  properties: {
    countryCode: str('Required. 2 or 3 letter ISO 3166-1 country code, for example US.'),
    streetNumber: str('Street number.'), streetName: str('Street name.'), crossStreet: str('Cross street name.'),
    municipality: str('City or town.'), municipalitySubdivision: str('District or sub-city.'),
    countrySubdivision: str('State or province, for example WA.'), countrySecondarySubdivision: str('County.'), countryTertiarySubdivision: str('Named area.'),
    postalCode: str('Postal code.'), limit: LIMIT, ofs: OFFSET, language: LANGUAGE, view: VIEW,
    entityType: enumOf('Limit geographic results to this level.', ['Country', 'CountrySubdivision', 'CountrySecondarySubdivision', 'CountryTertiarySubdivision', 'Municipality', 'MunicipalitySubdivision', 'Neighbourhood', 'PostalCodeArea']),
    extendedPostalCodesFor: EXTENDED_POSTAL
  },
  required: ['countryCode'],
  widget: 'places',
  async handler(args, ctx) {
    const data = await ctx.maps.json('/search/address/structured/json', {
      version: VERSIONS.searchV1,
      query: {
        countryCode: args.countryCode, streetNumber: args.streetNumber, streetName: args.streetName, crossStreet: args.crossStreet,
        municipality: args.municipality, municipalitySubdivision: args.municipalitySubdivision, countrySubdivision: args.countrySubdivision,
        countrySecondarySubdivision: args.countrySecondarySubdivision, countryTertiarySubdivision: args.countryTertiarySubdivision,
        postalCode: args.postalCode, limit: intInRange(args.limit, 'limit', 1, 100, 10), ofs: args.ofs, language: args.language, view: args.view,
        entityType: args.entityType, extendedPostalCodesFor: list(args.extendedPostalCodesFor).join(',') || undefined
      }
    });
    const found = (data.results || []).map(fromV1);
    return placesResult({ lead: `Found ${found.length} match(es) for the structured address.`, places: found, title: 'Structured address results' });
  }
}));

tools.push(defineTool({
  name: 'search_cross_street',
  title: 'Find the nearest cross street',
  description: 'Find the intersection (cross street) closest to a point, with the street names that meet there.',
  properties: {
    location: place('The point to look up.'), limit: LIMIT,
    heading: int('Direction of travel in degrees, 0 is north, 90 is east, to prefer the road the vehicle is on.', { minimum: 0, maximum: 360 }),
    radius: int('Search radius in meters.', { minimum: 1 }), language: LANGUAGE, view: VIEW
  },
  required: ['location'],
  widget: 'places',
  async handler(args, ctx) {
    const at = await ctx.places.resolve(args.location, 'location');
    const data = await ctx.maps.json('/search/address/reverse/crossStreet/json', {
      version: VERSIONS.searchV1,
      query: { query: [at.latitude, at.longitude], limit: intInRange(args.limit, 'limit', 1, 100, 5), heading: args.heading, radius: args.radius, language: args.language, view: args.view }
    });
    const found = (data.addresses || []).map((x) => {
      const [lat, lon] = String(x.position || '').split(',').map(Number);
      const a = x.address || {};
      return { name: a.streetName || a.freeformAddress || 'Intersection', address: a.freeformAddress, longitude: Number.isFinite(lon) ? lon : at.longitude, latitude: Number.isFinite(lat) ? lat : at.latitude, type: 'Cross street', locality: a.municipality, region: a.countrySubdivision, postalCode: a.postalCode, country: a.country };
    });
    return placesResult({ lead: `Found ${found.length} cross street(s) near ${labelOf(at, 'the point')}.`, places: found, title: 'Nearest cross streets', center: { longitude: at.longitude, latitude: at.latitude } });
  }
}));

/** The geometry for Search Inside Geometry: a circle, or a polygon of up to 50 points. */
function insideGeometry(args, centerPlace) {
  if (args.polygon && args.polygon.length) {
    let ring = args.polygon.map((p, i) => {
      const parsed = parsePlace(p, `polygon point ${i + 1}`);
      if (parsed.query) throw new InputError('Polygon points must be coordinates.');
      return [parsed.longitude, parsed.latitude];
    });
    if (ring.length < 3) throw new InputError('A polygon needs at least 3 points.');
    ring = simplifyLine(ring, 48);
    ring = [...ring, ring[0]];
    return { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'Polygon', coordinates: [roundLine(ring)] }, properties: {} }] };
  }
  if (centerPlace) {
    const radius = args.radiusMeters || 1000;
    return { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'Point', coordinates: [centerPlace.longitude, centerPlace.latitude] }, properties: { subType: 'Circle', radius } }] };
  }
  throw new InputError('Provide center (with radiusMeters) or polygon.');
}

tools.push(defineTool({
  name: 'search_inside_geometry',
  title: 'Search inside an area',
  description: 'Find places inside a circle (center and radius) or a polygon, for example all pharmacies inside a delivery zone. For "places I can reach in N minutes", use show_reachable_area instead.',
  properties: {
    query: str('What to look for, for example "pharmacy".'),
    center: place('Center of a circular area.'),
    radiusMeters: int('Radius of the circle in meters. Default 1000.', { minimum: 1 }),
    polygon: places('Corners of a polygon area, at least 3 and up to 50 points.', 3, 50),
    limit: LIMIT, categorySet: CATEGORY_SET, idxSet: strings('Indexes to search: Addr, Geo, PAD, POI, Str, Xstr.'), openingHours: OPENING_HOURS,
    language: LANGUAGE, view: VIEW, extendedPostalCodesFor: EXTENDED_POSTAL
  },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    const center = args.center ? await ctx.places.resolve(args.center, 'center') : undefined;
    const geometry = insideGeometry(args, center);
    const data = await ctx.maps.json('/search/geometry/json', {
      method: 'POST', version: VERSIONS.searchV1, body: { geometry },
      query: { query: args.query, limit: intInRange(args.limit, 'limit', 1, 100, 10), categorySet: list(args.categorySet).join(',') || undefined, idxSet: list(args.idxSet).join(',') || undefined, openingHours: args.openingHours, language: args.language, view: args.view, extendedPostalCodesFor: list(args.extendedPostalCodesFor).join(',') || undefined }
    });
    const found = (data.results || []).map(fromV1);
    const area = geometry.features[0].geometry.type === 'Polygon'
      ? { type: 'polygon', ring: geometry.features[0].geometry.coordinates[0] }
      : { type: 'circle', longitude: center.longitude, latitude: center.latitude, radius: args.radiusMeters || 1000 };
    const res = placesResult({ lead: `Found ${found.length} result(s) for "${args.query}" inside the area.`, places: found, title: `${args.query} in this area`, center: center ? { longitude: center.longitude, latitude: center.latitude } : undefined });
    res.structuredContent.area = area;
    return res;
  }
}));

tools.push(defineTool({
  name: 'search_along_route',
  title: 'Find places along a route',
  description: 'Find places such as gas stations, charging points or restaurants along a route, ranked by the extra driving time (detour) they add. Give origin and destination (the route is calculated for you) or the route line. Shows the route and the places on a map.',
  properties: {
    query: str('What to look for, for example "electric vehicle station" or "coffee".'),
    origin: place('Start of the route.'),
    destination: place('End of the route.'),
    route: places('Alternative to origin and destination: points of the route line in travel order, at least 2.', 2),
    maxDetourTimeSeconds: int('Maximum extra time in seconds a place may add, 1 to 3600. Default 600.', { minimum: 1, maximum: 3600 }),
    limit: int('Maximum results, 1 to 20. Default 10.', { minimum: 1, maximum: 20 }),
    categorySet: CATEGORY_SET, brandSet: BRAND_SET, connectorSet: CONNECTORS, openingHours: OPENING_HOURS, view: VIEW
  },
  required: ['query'],
  widget: 'places',
  async handler(args, ctx) {
    let line;
    if (args.route && args.route.length >= 2) {
      line = (await ctx.places.resolveAll(args.route, 'route point')).map((p) => [p.longitude, p.latitude]);
    } else if (args.origin && args.destination) {
      const [a, b] = await Promise.all([ctx.places.resolve(args.origin, 'origin'), ctx.places.resolve(args.destination, 'destination')]);
      const dir = await ctx.maps.json('/route/directions', {
        method: 'POST', version: VERSIONS.route, accept: 'application/geo+json', contentType: 'application/geo+json',
        body: { type: 'FeatureCollection', features: [a, b].map((p, i) => ({ type: 'Feature', geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] }, properties: { pointIndex: i, pointType: 'waypoint' } })), routeOutputOptions: ['routePath'] }
      });
      const path = (dir.features || []).find((f) => f.properties && f.properties.type === 'RoutePath');
      if (!path) throw new InputError('No route was found between the origin and destination.');
      line = flatten(path.geometry.coordinates);
    } else {
      throw new InputError('Provide origin and destination, or route.');
    }
    line = simplifyLine(line, 300);
    const data = await ctx.maps.json('/search/alongRoute/json', {
      method: 'POST', version: VERSIONS.searchV1, body: { route: { type: 'LineString', coordinates: roundLine(line) } },
      query: {
        query: args.query, maxDetourTime: intInRange(args.maxDetourTimeSeconds, 'maxDetourTimeSeconds', 1, 3600, 600), limit: intInRange(args.limit, 'limit', 1, 20, 10),
        categorySet: list(args.categorySet).join(',') || undefined, brandSet: list(args.brandSet).join(',') || undefined,
        connectorSet: list(args.connectorSet).join(',') || undefined, openingHours: args.openingHours, view: args.view
      }
    });
    const found = (data.results || []).map(fromV1);
    return placesResult({ lead: `Found ${found.length} place(s) for "${args.query}" within a ${Math.round((args.maxDetourTimeSeconds || 600) / 60)} minute detour of the route.`, places: found, title: `${args.query} along the route`, route: roundLine(simplifyLine(line, 150), 4) });
  }
}));

let categoryCache = new Map();

tools.push(defineTool({
  name: 'get_poi_categories',
  title: 'Look up point-of-interest categories',
  description: 'Look up the category IDs and names used by the POI searches. Give a filter such as "restaurant" or "charging" to find matching categories, or leave it blank for the top-level categories.',
  properties: {
    filter: str('Part of a category name or synonym to look for, for example "charging".'),
    limit: int('Maximum categories to return. Default 25.', { minimum: 1, maximum: 200 }),
    language: LANGUAGE
  },
  async handler(args, ctx) {
    const language = args.language || 'en-US';
    if (!categoryCache.has(language)) {
      const data = await ctx.maps.json('/search/poi/category/tree/json', { version: VERSIONS.searchV1, query: { language } });
      categoryCache.set(language, data.poiCategories || []);
      if (categoryCache.size > 6) categoryCache = new Map([...categoryCache].slice(-3));
    }
    const all = categoryCache.get(language);
    const filter = (args.filter || '').toLowerCase();
    const limit = args.limit || 25;
    const rows = (filter ? all.filter((c) => c.name.toLowerCase().includes(filter) || (c.synonyms || []).some((s) => s.toLowerCase().includes(filter))) : all.filter((c) => !c.parentCategoryId))
      .slice(0, limit).map((c) => ({ id: c.id, name: c.name, ...(c.synonyms && c.synonyms.length ? { synonyms: c.synonyms.slice(0, 4) } : {}), ...(c.childCategoryIds && c.childCategoryIds.length ? { childCategoryIds: c.childCategoryIds.slice(0, 20) } : {}), ...(c.parentCategoryId ? { parentCategoryId: c.parentCategoryId } : {}) }));
    return dataResult(`${rows.length} categor${rows.length === 1 ? 'y' : 'ies'}${filter ? ` matching "${args.filter}"` : ' (top level)'} of ${all.length}. Use the id in categorySet.`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_polygons',
  title: 'Get polygons for earlier search results',
  description: 'Get the outline polygons for geometry IDs returned by earlier searches (the geometryId on a geographic result), for example a city or postal code boundary.',
  properties: { geometries: strings('Geometry IDs from earlier search results, up to 20.'), zoom: int('Resolution 0 to 20 that controls the level of detail. Default 12.', { minimum: 0, maximum: 20 }) },
  required: ['geometries'],
  widget: 'map',
  async handler(args, ctx) {
    const ids = list(args.geometries).slice(0, 20);
    if (!ids.length) throw new InputError('Provide at least one geometry ID.');
    const data = await ctx.maps.json('/search/polygon/json', { version: VERSIONS.searchV1, query: { geometries: ids, zoom: args.zoom ?? 12 } });
    const polys = (data.additionalData || []).filter((d) => d.geometryData && d.geometryData.features);
    const palette = ['#0078D4', '#F7630C', '#8764B8', '#038387', '#C239B3'];
    const layers = [];
    polys.forEach((d, i) => {
      const feature = d.geometryData.features[0];
      if (!feature) return;
      layers.push({ id: d.providerID || `p${i}`, type: 'polygon', name: `Polygon ${i + 1}`, geometry: simplifyGeometry(feature.geometry, 300), color: palette[i % palette.length], fillOpacity: 0.18 });
    });
    const bbox = bboxOf(layers.flatMap((l) => flatten(l.geometry.coordinates)));
    return ok(`Returned ${layers.length} polygon(s).\n${JSON.stringify(layers.map((l) => ({ id: l.id, geometry: l.geometry })))}`, { kind: 'map', title: 'Search polygons', layers, bbox });
  }
}));

tools.push(defineTool({
  name: 'batch_search_fuzzy',
  title: 'Run many searches at once',
  description: 'Run up to 100 fuzzy searches in a single request and get the top results for each. Use for bulk lookups of place names or addresses.',
  properties: {
    queries: arrayOf('The searches to run, up to 100.', { type: 'string' }),
    limit: int('Maximum results per search, 1 to 20. Default 1.', { minimum: 1, maximum: 20 }),
    near: NEAR, language: LANGUAGE
  },
  required: ['queries'],
  async handler(args, ctx) {
    const queries = list(args.queries);
    if (!queries.length || queries.length > 100) throw new InputError('Provide between 1 and 100 queries.');
    const near = args.near ? await ctx.places.resolve(args.near, 'near') : undefined;
    const limit = intInRange(args.limit, 'limit', 1, 20, 1);
    const data = await ctx.maps.batch(queries, (query) => ctx.maps.json('/search/fuzzy/json', {
      version: VERSIONS.searchV1, language: args.language,
      query: { query, limit, lat: near && near.latitude, lon: near && near.longitude }
    }));
    const rows = (data.batchItems || []).map((item, i) => ({ query: queries[i], ...(item.error ? { error: item.error.message } : {}), results: (item.results || []).map(fromV1) }));
    return dataResult(`Ran ${rows.length} search(es).`, rows);
  }
}));

module.exports = { tools, fromFeature, fromV1, placesResult, v1Params };
