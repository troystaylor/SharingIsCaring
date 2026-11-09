'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult, jsonText } = require('../define');
const { str, num, int, bool, enumOf, arrayOf, strings, place, places } = require('../schema');
const { InputError, intInRange, oneOf, list, labelOf, parsePlace } = require('../args');
const { flatten, bboxOf, simplifyLine, roundLine, round, geometryAreaSqm, simplifyGeometry, formatDuration, formatDistance, haversine } = require('../geo');

const TRAVEL_MODES = ['driving', 'truck', 'walking'];
const OPTIMIZE_MODES = ['short', 'shortest', 'fastestWithoutTraffic', 'fastestWithTraffic'];
const AVOID_VALUES = ['limitedAccessHighways', 'tollRoads', 'ferries', 'tunnels', 'borderCrossings', 'lowEmissionZones', 'unpavedRoads'];
const GEOJSON = 'application/geo+json';
const MAX_DIRECTIONS_CHARS = 60000;
const STEP_KINDS = new Set(['Waypoint', 'ManeuverPoint']);

const TRAVEL_MODE = enumOf('Mode of travel. Default driving.', TRAVEL_MODES);
const OPTIMIZE = enumOf('Route optimization. fastestWithTraffic uses live traffic now and predicted traffic for a future departAt. Default fastestWithoutTraffic.', OPTIMIZE_MODES);
const AVOID = strings(`Road features to avoid. Values: ${AVOID_VALUES.join(', ')}.`);
const DEPART_AT = str('Departure time, ISO 8601 with a UTC offset, for example 2026-11-09T08:00:00-08:00. Cannot be combined with arriveAt.');
const ARRIVE_AT = str('Arrival time, ISO 8601 with a UTC offset. Cannot be combined with departAt.');
const UNITS = enumOf('Distance units to show: imperial (miles) or metric (kilometers). Default metric.', ['imperial', 'metric']);
const LANGUAGE = str('Language for instructions and names, for example en-US.');
const VEHICLE = {
  type: 'object',
  description: 'Truck attributes, used when travelMode is truck, so routes avoid low bridges and restricted roads.',
  properties: {
    commercial: bool('The vehicle is used commercially.'),
    lengthMeters: num('Length in meters.'),
    widthMeters: num('Width in meters.'),
    heightMeters: num('Height in meters.'),
    weightKg: int('Gross weight in kilograms.'),
    axleWeightKg: int('Weight per axle in kilograms.'),
    maxSpeedKmh: int('Maximum speed in kilometers per hour.'),
    loadTypes: strings('Hazardous cargo classes, for example USHazmatClass1 or otherHazmatGeneral.')
  }
};

function vehicleSpec(vehicle) {
  if (!vehicle) return undefined;
  const spec = {
    isVehicleCommercial: vehicle.commercial, length: vehicle.lengthMeters, width: vehicle.widthMeters, height: vehicle.heightMeters,
    weight: vehicle.weightKg, axleWeight: vehicle.axleWeightKg, maxSpeed: vehicle.maxSpeedKmh,
    loadType: list(vehicle.loadTypes).length ? list(vehicle.loadTypes) : undefined
  };
  const clean = Object.fromEntries(Object.entries(spec).filter(([, v]) => v !== undefined));
  return Object.keys(clean).length ? clean : undefined;
}

const point = (lon, lat, properties = {}) => ({ type: 'Feature', geometry: { type: 'Point', coordinates: [lon, lat] }, properties });

function assertTimes(args) {
  if (args.departAt && args.arriveAt) throw new InputError('Use departAt or arriveAt, not both.');
}

function avoidFor(args) {
  const avoid = list(args.avoid);
  for (const a of avoid) if (!AVOID_VALUES.includes(a)) throw new InputError(`avoid values must be from: ${AVOID_VALUES.join(', ')}.`);
  return avoid.length ? avoid : undefined;
}

function directionsBody({ stops, args, outputs, maxRouteCount }) {
  assertTimes(args);
  const body = {
    type: 'FeatureCollection',
    features: stops.map((s, i) => point(s.longitude, s.latitude, { pointIndex: i, pointType: 'waypoint' })),
    routeOutputOptions: outputs
  };
  const mode = oneOf(args.travelMode, 'travelMode', TRAVEL_MODES);
  if (mode) body.travelMode = mode;
  const optimize = oneOf(args.optimizeRoute, 'optimizeRoute', OPTIMIZE_MODES);
  if (optimize) body.optimizeRoute = optimize;
  if (args.optimizeWaypointOrder) body.optimizeWaypointOrder = true;
  if (args.departAt) body.departAt = args.departAt;
  if (args.arriveAt) body.arriveAt = args.arriveAt;
  const avoid = avoidFor(args);
  if (avoid) body.avoid = avoid;
  const maxCount = maxRouteCount ?? args.maxRouteCount;
  if (maxCount) body.maxRouteCount = intInRange(maxCount, 'maxRouteCount', 1, 6, 1);
  const spec = vehicleSpec(args.vehicle);
  if (spec) body.vehicleSpec = spec;
  return body;
}

/** Calls Route Directions. Returns the raw GeoJSON response. */
function fetchDirections(ctx, { stops, args, outputs = ['routeSummary'], maxRouteCount }) {
  return ctx.maps.json('/route/directions', {
    method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON,
    language: args.language, body: directionsBody({ stops, args, outputs, maxRouteCount })
  });
}

async function resolveStops(args, ctx) {
  const origin = await ctx.places.resolve(args.origin, 'origin');
  const rest = await Promise.all([
    ...(args.waypoints || []).map((w, i) => ctx.places.resolve(w, `waypoint ${i + 1}`)),
    ctx.places.resolve(args.destination, 'destination')
  ]);
  return [origin, ...rest];
}

/** Turns one route FeatureCollection into a compact route object. */
function parseRoute(collection, { lineMax = 400, stepMax = 150 } = {}) {
  const features = collection.features || [];
  const path = features.find((f) => f.properties && f.properties.type === 'RoutePath');
  if (!path) return null;
  const p = path.properties;
  const full = flatten(path.geometry.coordinates);
  const stepPoints = features.filter((f) => f.geometry && f.geometry.type === 'Point' && f.properties && STEP_KINDS.has(f.properties.type));
  const steps = stepPoints.map((f, i) => {
    const sp = f.properties;
    const ins = sp.instruction || {};
    return {
      text: ins.text || '',
      maneuver: ins.maneuverType || '',
      meters: Math.round(sp.distanceInMeters || 0),
      seconds: Math.round(sp.durationInSeconds || 0),
      longitude: round(f.geometry.coordinates[0], 5),
      latitude: round(f.geometry.coordinates[1], 5),
      ...(sp.type === 'Waypoint' ? { stop: sp.order ? sp.order.inputIndex : i } : {}),
      ...(sp.towardsRoadName ? { road: sp.towardsRoadName } : {})
    };
  });
  return {
    meters: Math.round(p.distanceInMeters || 0),
    seconds: Math.round(p.durationInSeconds || 0),
    trafficSeconds: p.durationTrafficInSeconds !== undefined ? Math.round(p.durationTrafficInSeconds) : undefined,
    departAt: p.departureAt,
    arriveAt: p.arrivalAt,
    congestion: p.trafficCongestion,
    legs: (p.legs || []).map((l) => ({ meters: Math.round(l.distanceInMeters || 0), seconds: Math.round(l.durationInSeconds || 0), trafficSeconds: l.durationTrafficInSeconds !== undefined ? Math.round(l.durationTrafficInSeconds) : undefined })),
    line: roundLine(simplifyLine(full, lineMax)),
    lineFull: full,
    steps: steps.length > stepMax ? steps.slice(0, stepMax) : steps,
    stepCount: steps.length
  };
}

/** The best route first, then the alternatives. */
function parseRoutes(json, options) {
  const routes = [parseRoute(json, options), ...(json.alternativeRoutes || []).map((r) => parseRoute(r, options))].filter(Boolean);
  routes.forEach((r, i) => { r.index = i; });
  return routes;
}

const fmt = (n) => Number(n.toFixed(6)).toString();

function bingRouteUrl(stops, travelMode) {
  const p = (s) => `pos.${fmt(s.latitude)}_${fmt(s.longitude)}`;
  return `https://www.bing.com/maps?rtp=${stops.map(p).join('~')}&mode=${travelMode === 'walking' ? 'W' : 'D'}`;
}

function stopSummary(s) {
  return { longitude: s.longitude, latitude: s.latitude, name: s.name || s.query };
}

const tools = [];

tools.push(defineTool({
  name: 'get_route_directions',
  title: 'Get directions',
  description: 'Get driving, truck or walking directions between two places, with optional stops and alternative routes. Accepts addresses or place names as well as coordinates. Returns distance, travel time (with traffic when optimizeRoute is fastestWithTraffic) and turn-by-turn steps, and shows the routes on an interactive map where the user can switch alternatives, mode and road options. For several independent routes use get_route_directions_batch; for departure-time advice use compare_departure_times.',
  properties: {
    origin: place('Where the route starts.'),
    destination: place('Where the route ends.'),
    waypoints: places('Optional stops between origin and destination, in order.'),
    travelMode: TRAVEL_MODE,
    optimizeRoute: OPTIMIZE,
    optimizeWaypointOrder: bool('Reorder the waypoints to reduce the route cost.'),
    departAt: DEPART_AT,
    arriveAt: ARRIVE_AT,
    avoid: AVOID,
    vehicle: VEHICLE,
    maxRouteCount: int('Number of routes to return, including alternatives, 1 to 6. Default 3.', { minimum: 1, maximum: 6 }),
    includeSteps: bool('Include turn-by-turn steps for the first route in the text. Default true.'),
    includeGeoJson: bool('Also return the raw GeoJSON from Azure Maps for the first route. Large. Default false.'),
    units: UNITS,
    language: LANGUAGE
  },
  required: ['origin', 'destination'],
  widget: 'route',
  async handler(args, ctx) {
    const stops = await resolveStops(args, ctx);
    const json = await fetchDirections(ctx, { stops, args, outputs: ['routeSummary', 'routePath', 'itinerary'], maxRouteCount: args.maxRouteCount || 3 });
    const routes = parseRoutes(json);
    if (!routes.length) throw new InputError('No route was found between those places.');

    const units = args.units || 'metric';
    const describe = (r, i) => `Route ${i + 1}: ${formatDistance(r.meters, units)}, ${formatDuration(r.trafficSeconds ?? r.seconds)}${r.trafficSeconds !== undefined ? ' with traffic' : ''}${r.congestion ? ` (${r.congestion} congestion)` : ''}`;
    const lines = [`Directions from ${labelOf(stops[0])} to ${labelOf(stops[stops.length - 1])}${stops.length > 2 ? ` via ${stops.length - 2} stop(s)` : ''}:`, ...routes.map(describe)];
    if (args.includeSteps !== false) {
      let n = 0;
      lines.push('', 'Turn-by-turn for route 1:');
      for (const s of routes[0].steps) lines.push(`${s.stop !== undefined ? '•' : `${++n}.`} ${s.text}${s.meters ? ` (${formatDistance(s.meters, units)})` : ''}`);
      if (routes[0].stepCount > routes[0].steps.length) lines.push(`…${routes[0].stepCount - routes[0].steps.length} more steps.`);
    }
    const bingUrl = bingRouteUrl(stops, args.travelMode);
    lines.push('', `Bing Maps link: ${bingUrl}`, 'An interactive map shows the routes when the client supports it. If the user cannot see it, share the summary and the Bing Maps link; do not draw a map yourself.');
    let text = lines.join('\n');
    if (args.includeGeoJson) {
      const { text: geo } = jsonText(json, MAX_DIRECTIONS_CHARS);
      text += `\n\nGeoJSON:\n${geo}`;
    }

    const request = {
      travelMode: args.travelMode || 'driving', optimizeRoute: args.optimizeRoute || 'fastestWithoutTraffic',
      avoid: avoidFor(args) || [], departAt: args.departAt, arriveAt: args.arriveAt, units
    };
    return ok(text, {
      kind: 'route',
      stops: stops.map(stopSummary),
      routes: routes.map(({ lineFull, ...r }) => r),
      request, bingUrl
    });
  }
}));

const MAX_BATCH = 100;

tools.push(defineTool({
  name: 'get_route_directions_batch',
  title: 'Get directions for many trips',
  description: `Calculate up to ${MAX_BATCH} independent routes in one request and get a distance and time for each. Each trip has an origin and destination. Use for comparing many trips, such as many customers to one depot.`,
  properties: {
    trips: arrayOf(`The trips, up to ${MAX_BATCH}.`, {
      type: 'object',
      properties: { id: str('Your label for the trip.'), origin: place('Start.'), destination: place('End.') }
    }),
    travelMode: TRAVEL_MODE, optimizeRoute: OPTIMIZE, departAt: DEPART_AT, arriveAt: ARRIVE_AT, avoid: AVOID, vehicle: VEHICLE, units: UNITS, language: LANGUAGE
  },
  required: ['trips'],
  async handler(args, ctx) {
    const trips = args.trips || [];
    if (!trips.length || trips.length > MAX_BATCH) throw new InputError(`Provide between 1 and ${MAX_BATCH} trips.`);
    const resolved = await Promise.all(trips.map(async (t, i) => ({ id: t.id || String(i + 1), origin: await ctx.places.resolve(t.origin, `trip ${i + 1} origin`), destination: await ctx.places.resolve(t.destination, `trip ${i + 1} destination`) })));
    await ctx.places.nameAll(resolved.flatMap((t) => [t.origin, t.destination]));
    const data = await ctx.maps.batch(resolved, (t) => ctx.maps.json('/route/directions', {
      method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, language: args.language,
      body: directionsBody({ stops: [t.origin, t.destination], args, outputs: ['routeSummary'], maxRouteCount: 1 })
    }));
    const units = args.units || 'metric';
    const rows = (data.batchItems || []).map((item, i) => {
      const r = parseRoute(item);
      const t = resolved[i];
      const row = { id: t.id, origin: labelOf(t.origin), destination: labelOf(t.destination) };
      if (!r) return { ...row, ok: false, error: (item.error && item.error.message) || 'No route found' };
      return { ...row, ok: true, meters: r.meters, seconds: r.seconds, ...(r.trafficSeconds !== undefined ? { trafficSeconds: r.trafficSeconds } : {}), distance: formatDistance(r.meters, units), duration: formatDuration(r.trafficSeconds ?? r.seconds) };
    });
    return dataResult(`Calculated ${rows.filter((r) => r.ok).length} of ${rows.length} route(s).`, rows);
  }
}));

// ───────────────────────────── matrix ─────────────────────────────

const MATRIX_MAX_CELLS = 2500;

async function matrixBody(args, ctx) {
  const origins = await ctx.places.resolveAll(args.origins, 'origin');
  const destinations = await ctx.places.resolveAll(args.destinations, 'destination');
  await Promise.all([ctx.places.nameAll(origins), ctx.places.nameAll(destinations)]);
  assertTimes(args);
  const body = {
    type: 'FeatureCollection',
    features: [
      { type: 'Feature', geometry: { type: 'MultiPoint', coordinates: origins.map((p) => [p.longitude, p.latitude]) }, properties: { pointType: 'origins' } },
      { type: 'Feature', geometry: { type: 'MultiPoint', coordinates: destinations.map((p) => [p.longitude, p.latitude]) }, properties: { pointType: 'destinations' } }
    ],
    optimizeRoute: args.optimizeRoute || 'fastest'
  };
  const mode = oneOf(args.travelMode, 'travelMode', TRAVEL_MODES);
  if (mode) body.travelMode = mode;
  if (args.traffic) body.traffic = oneOf(args.traffic, 'traffic', ['historical', 'live']);
  if (args.departAt) body.departAt = args.departAt;
  if (args.arriveAt) body.arriveAt = args.arriveAt;
  const avoid = list(args.avoid);
  if (avoid.length) body.avoid = avoid;
  const spec = vehicleSpec(args.vehicle);
  if (spec) body.vehicleSpec = spec;
  return { body, origins, destinations };
}

function matrixResult({ data, origins, destinations, args }) {
  const props = data.properties || {};
  const duration = origins.map(() => destinations.map(() => null));
  const distance = origins.map(() => destinations.map(() => null));
  const traffic = origins.map(() => destinations.map(() => null));
  for (const cell of props.matrix || []) {
    if (cell.statusCode && cell.statusCode !== 200) continue;
    duration[cell.originIndex][cell.destinationIndex] = Math.round(cell.durationInSeconds);
    distance[cell.originIndex][cell.destinationIndex] = Math.round(cell.distanceInMeters);
    if (cell.durationTrafficInSeconds !== undefined) traffic[cell.originIndex][cell.destinationIndex] = Math.round(cell.durationTrafficInSeconds);
  }
  const names = (list) => list.map((p, i) => labelOf(p, `Point ${i + 1}`));
  const units = args.units || 'metric';
  const structured = {
    kind: 'matrix',
    origins: origins.map((p, i) => ({ name: names(origins)[i], longitude: p.longitude, latitude: p.latitude })),
    destinations: destinations.map((p, i) => ({ name: names(destinations)[i], longitude: p.longitude, latitude: p.latitude })),
    durationSeconds: duration, distanceMeters: distance, trafficSeconds: traffic.some((r) => r.some((v) => v !== null)) ? traffic : undefined,
    summary: props.summary || {}, units, travelMode: args.travelMode || 'driving'
  };
  const lines = origins.map((_, o) => `${names(origins)[o]}: ` + destinations.map((_, d) => `${names(destinations)[d]} ${duration[o][d] === null ? 'n/a' : `${formatDuration(duration[o][d])}, ${formatDistance(distance[o][d], units)}`}`).join(' | '));
  const text = `Route matrix for ${origins.length} origin(s) by ${destinations.length} destination(s): ${(props.summary || {}).successfulCount ?? '?'} of ${(props.summary || {}).totalCount ?? origins.length * destinations.length} routes found.\n${lines.join('\n')}\nA heat map table is shown when the client supports it.`;
  return ok(text, structured);
}

tools.push(defineTool({
  name: 'get_route_matrix',
  title: 'Get a travel time and distance matrix',
  description: `Calculate the travel time and distance between every origin and every destination, for example to find the closest depot for each customer. Accepts addresses or coordinates. Origins times destinations must not exceed ${MATRIX_MAX_CELLS}; use start_route_matrix_async for larger matrices. Shows a heat-map table with the closest origin for each destination.`,
  properties: {
    origins: places('The origins.', 1),
    destinations: places('The destinations.', 1),
    travelMode: TRAVEL_MODE,
    traffic: enumOf('Traffic model: historical (default) or live.', ['historical', 'live']),
    departAt: str('Departure time, ISO 8601 with a UTC offset, or "now" with live traffic.'),
    arriveAt: ARRIVE_AT,
    avoid: strings('Features to avoid: tollRoads, unpavedRoads.'),
    vehicle: VEHICLE, units: UNITS
  },
  required: ['origins', 'destinations'],
  widget: 'matrix',
  async handler(args, ctx) {
    const { body, origins, destinations } = await matrixBody(args, ctx);
    if (origins.length * destinations.length > MATRIX_MAX_CELLS) throw new InputError(`Origins times destinations must not exceed ${MATRIX_MAX_CELLS}. Use start_route_matrix_async for larger matrices.`);
    const data = await ctx.maps.json('/route/matrix', { method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, body });
    return matrixResult({ data, origins, destinations, args });
  }
}));

// The points of recent asynchronous matrices, so the finished matrix can show names and a map. Kept in memory only.
const operationPoints = new Map();
const OPERATION_MEMORY = 200;

const pointInfo = (p, i, fallback) => ({ name: labelOf(p, `${fallback} ${i + 1}`), longitude: p.longitude, latitude: p.latitude });

tools.push(defineTool({
  name: 'start_route_matrix_async',
  title: 'Start a large route matrix',
  description: 'Start an asynchronous route matrix for large sets of origins and destinations. Returns an operation ID: call get_route_operation with it until the operation completes. Supports fastest, shortest and traffic-aware optimization.',
  properties: {
    origins: places('The origins.', 1),
    destinations: places('The destinations.', 1),
    travelMode: TRAVEL_MODE,
    optimizeRoute: enumOf('Optimization. Default fastest.', ['fastest', 'shortest', 'fastestWithoutTraffic', 'fastestWithTraffic']),
    traffic: enumOf('Traffic model: historical (default) or live.', ['historical', 'live']),
    departAt: str('Departure time, ISO 8601 with a UTC offset.'), arriveAt: ARRIVE_AT,
    avoid: strings('Features to avoid: tollRoads, unpavedRoads.'), vehicle: VEHICLE
  },
  required: ['origins', 'destinations'],
  async handler(args, ctx) {
    const { body, origins, destinations } = await matrixBody(args, ctx);
    const data = await ctx.maps.json('/route/matrix:async', { method: 'POST', version: VERSIONS.route, accept: 'application/json', contentType: GEOJSON, body });
    const location = data.location || '';
    const match = /operations\/([^/?]+)/.exec(location);
    if (!match) throw new Error('Azure Maps accepted the matrix but did not return an operation ID.');
    operationPoints.set(match[1], { origins: origins.map((p, i) => pointInfo(p, i, 'Origin')), destinations: destinations.map((p, i) => pointInfo(p, i, 'Destination')) });
    while (operationPoints.size > OPERATION_MEMORY) operationPoints.delete(operationPoints.keys().next().value);
    return ok(`Started route matrix operation ${match[1]}. Call get_route_operation with this ID to check progress and get the result.`, { kind: 'operation', operationId: match[1] });
  }
}));

tools.push(defineTool({
  name: 'get_route_operation',
  title: 'Check an asynchronous route operation',
  description: 'Get the status of an asynchronous route matrix started with start_route_matrix_async, and its result when it has completed. The result shows place names and a map when the matrix was started by this server. If the server was restarted, pass the same origins and destinations again to get names.',
  properties: {
    operationId: str('The operation ID returned by start_route_matrix_async.'),
    origins: places('Optional. The origins the matrix was started with, to label the result.'),
    destinations: places('Optional. The destinations the matrix was started with, to label the result.'),
    units: UNITS
  },
  required: ['operationId'],
  widget: 'matrix',
  async handler(args, ctx) {
    const id = encodeURIComponent(args.operationId);
    const status = await ctx.maps.json(`/route/operations/${id}`, { version: VERSIONS.route });
    if (status.status !== 'Completed') {
      return ok(`Operation ${args.operationId} is ${status.status}${status.error ? `: ${status.error.message}` : ''}.${status.status === 'Failed' ? '' : ' Check again in a few seconds.'}`, { kind: 'operation', operationId: args.operationId, status: status.status });
    }
    const data = await ctx.maps.json(`/route/operations/${id}/result`, { version: VERSIONS.route, accept: GEOJSON });
    const props = data.properties || {};
    const cells = props.matrix || [];
    const rows = Math.max(0, ...cells.map((c) => c.originIndex)) + 1;
    const cols = Math.max(0, ...cells.map((c) => c.destinationIndex)) + 1;
    const duration = Array.from({ length: rows }, () => Array(cols).fill(null));
    const distance = Array.from({ length: rows }, () => Array(cols).fill(null));
    for (const c of cells) {
      if (c.statusCode && c.statusCode !== 200) continue;
      duration[c.originIndex][c.destinationIndex] = Math.round(c.durationInSeconds);
      distance[c.originIndex][c.destinationIndex] = Math.round(c.distanceInMeters);
    }

    let known = operationPoints.get(args.operationId);
    if (!known && args.origins && args.destinations) {
      const o = await ctx.places.resolveAll(args.origins, 'origin');
      const d = await ctx.places.resolveAll(args.destinations, 'destination');
      await Promise.all([ctx.places.nameAll(o), ctx.places.nameAll(d)]);
      known = { origins: o.map((p, i) => pointInfo(p, i, 'Origin')), destinations: d.map((p, i) => pointInfo(p, i, 'Destination')) };
    }
    const label = (list, count, fallback) => Array.from({ length: count }, (_, i) => (list && list[i]) || { name: `${fallback} ${i + 1}` });
    const origins = label(known && known.origins, rows, 'Origin');
    const destinations = label(known && known.destinations, cols, 'Destination');

    const units = args.units || 'metric';
    const structured = { kind: 'matrix', origins, destinations, durationSeconds: duration, distanceMeters: distance, summary: props.summary || {}, units };
    const text = `Operation ${args.operationId} completed: ${(props.summary || {}).successfulCount ?? '?'} of ${(props.summary || {}).totalCount ?? cells.length} routes found.\n`
      + (known ? `Origins: ${origins.map((p) => p.name).join('; ')}\nDestinations: ${destinations.map((p) => p.name).join('; ')}\n` : '')
      + jsonText({ durationSeconds: duration, distanceMeters: distance }).text;
    return ok(text, structured);
  }
}));

// ───────────────────────────── range (isochrones) ─────────────────────────────

const RANGE_COLORS = ['#107C10', '#0078D4', '#8764B8', '#F7630C', '#C239B3', '#C50F1F'];
const RANGE_MODES = ['driving', 'truck'];
const RANGE_OPTIMIZE = ['shortest', 'fastestWithoutTraffic', 'fastestWithTraffic'];

function rangeBody({ origin, budget, args }) {
  const properties = {};
  if (budget.type === 'time') properties.timeBudgetInSec = budget.value; else properties.distanceBudgetInMeters = budget.value;
  const mode = oneOf(args.travelMode, 'travelMode', RANGE_MODES);
  if (mode) properties.travelMode = mode;
  const optimize = oneOf(args.optimizeRoute, 'optimizeRoute', RANGE_OPTIMIZE);
  if (optimize) properties.optimizeRoute = optimize;
  if (args.departAt) properties.departAt = args.departAt;
  const avoid = list(args.avoid);
  if (avoid.length) properties.avoid = avoid;
  const spec = vehicleSpec(args.vehicle);
  if (spec) properties.vehicleSpec = spec;
  return point(origin.longitude, origin.latitude, properties);
}

function polygonOf(json) {
  const feature = (json.features || []).find((f) => f.geometry && (f.geometry.type === 'Polygon' || f.geometry.type === 'MultiPolygon')) || (json.type === 'Feature' && json);
  return feature && feature.geometry;
}

/** Reads the budgets the caller asked for: several time bands, one time budget, or one distance budget. */
function budgetsOf(args) {
  if (list(args.timeBudgetsMinutes).length) {
    return list(args.timeBudgetsMinutes).slice(0, 6).map((m) => {
      const minutes = Number(m);
      if (!Number.isFinite(minutes) || minutes <= 0) throw new InputError('timeBudgetsMinutes must be positive numbers.');
      return { type: 'time', value: Math.round(minutes * 60), label: `${minutes} min` };
    }).sort((a, b) => a.value - b.value);
  }
  if (args.timeBudgetInSec) return [{ type: 'time', value: Math.round(Number(args.timeBudgetInSec)), label: formatDuration(Number(args.timeBudgetInSec)) }];
  if (args.distanceBudgetInMeters) return [{ type: 'distance', value: Math.round(Number(args.distanceBudgetInMeters)), label: formatDistance(Number(args.distanceBudgetInMeters), args.units) }];
  throw new InputError('Provide timeBudgetsMinutes, timeBudgetInSec or distanceBudgetInMeters.');
}

const REACH_PROPERTIES = {
  origin: place('The starting point.'),
  timeBudgetsMinutes: arrayOf('Time budgets in minutes. Give several, such as [5, 10, 15], to see nested reachable areas. Up to 6.', { type: 'number' }),
  timeBudgetInSec: num('A single time budget in seconds. Alternative to timeBudgetsMinutes.'),
  distanceBudgetInMeters: num('A distance budget in meters. Provide a time or a distance budget, not both.'),
  travelMode: enumOf('Mode of travel. Default driving.', RANGE_MODES),
  optimizeRoute: enumOf('Route optimization. Default fastestWithoutTraffic.', RANGE_OPTIMIZE),
  departAt: str('Departure time, ISO 8601 with a UTC offset, to use traffic for that time.'),
  avoid: AVOID, vehicle: VEHICLE, units: UNITS
};

async function reach(ctx, origin, budgets, args) {
  const results = await Promise.all(budgets.map(async (budget, i) => {
    const json = await ctx.maps.json('/route/range', { method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, body: rangeBody({ origin, budget, args }) });
    const geometry = polygonOf(json);
    if (!geometry) return { ...budget, color: RANGE_COLORS[i % RANGE_COLORS.length], empty: true };
    return {
      ...budget, color: RANGE_COLORS[i % RANGE_COLORS.length],
      geometry: simplifyGeometry(geometry, 200),
      areaSqKm: round(geometryAreaSqm(geometry) / 1e6, 1)
    };
  }));
  return results;
}

tools.push(defineTool({
  name: 'get_route_range',
  title: 'Get the area reachable in a time or distance',
  description: 'Get the area reachable from a starting point within a time or distance budget (an isochrone), for example everywhere within 10, 20 and 30 minutes of a store. Accepts an address or coordinates. Shows nested colored areas on an interactive map where the user can change the time, mode and start point, and find places inside the area. Use placesQuery to also list places (for example "pharmacy") that fall inside the largest area.',
  properties: { ...REACH_PROPERTIES, placesQuery: str('Optional. Also search for places like "pharmacy" inside the largest reachable area.') },
  required: ['origin'],
  widget: 'reach',
  async handler(args, ctx) {
    const origin = await ctx.places.resolve(args.origin, 'origin');
    const budgets = budgetsOf(args);
    const bands = await reach(ctx, origin, budgets, args);
    const drawn = bands.filter((b) => b.geometry);
    if (!drawn.length) throw new InputError('No reachable area was returned for that starting point.');

    let places;
    if (args.placesQuery) {
      const outer = drawn[drawn.length - 1];
      const ring = simplifyLine(outer.geometry.coordinates[0][0], 48);
      const data = await ctx.maps.json('/search/geometry/json', {
        method: 'POST', version: VERSIONS.searchV1,
        body: { geometry: { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'Polygon', coordinates: [roundLine([...ring, ring[0]])] }, properties: {} }] } },
        query: { query: args.placesQuery, limit: 20 }
      });
      const { fromV1 } = require('./search');
      places = (data.results || []).map(fromV1);
    }

    const units = args.units || 'metric';
    const lines = bands.map((b) => (b.geometry ? `${b.label}: about ${b.areaSqKm.toLocaleString('en-US')} square kilometers` : `${b.label}: no area`));
    let text = `Reachable from ${labelOf(origin, 'the origin')} by ${args.travelMode || 'driving'}:\n${lines.join('\n')}`;
    if (places) text += `\n\n${places.length} "${args.placesQuery}" place(s) inside the ${drawn[drawn.length - 1].label} area:\n${places.map((p, i) => `${i + 1}. ${p.name}${p.address ? ` — ${p.address}` : ''}`).join('\n')}`;
    text += '\nAn interactive map shows the areas when the client supports it.';

    const all = drawn.flatMap((b) => flatten(b.geometry.coordinates));
    return ok(text, {
      kind: 'reach', origin: { longitude: origin.longitude, latitude: origin.latitude, name: labelOf(origin, 'Start') },
      bands, request: { travelMode: args.travelMode || 'driving', optimizeRoute: args.optimizeRoute || 'fastestWithoutTraffic', departAt: args.departAt, avoid: list(args.avoid), units },
      bbox: bboxOf(all), ...(places ? { placesQuery: args.placesQuery, places } : {})
    });
  }
}));

tools.push(defineTool({
  name: 'get_route_range_batch',
  title: 'Get reachable areas for many origins',
  description: 'Calculate the area reachable from each of up to 100 origins within the same time or distance budget in one request, for example the 15-minute coverage of every store.',
  properties: {
    origins: places('The starting points.', 1, MAX_BATCH),
    timeBudgetInSec: num('Time budget in seconds.'), distanceBudgetInMeters: num('Distance budget in meters.'),
    travelMode: enumOf('Mode of travel. Default driving.', RANGE_MODES), optimizeRoute: enumOf('Route optimization.', RANGE_OPTIMIZE), departAt: str('Departure time, ISO 8601 with a UTC offset.'), avoid: AVOID
  },
  required: ['origins'],
  widget: 'map',
  async handler(args, ctx) {
    const origins = await ctx.places.resolveAll(args.origins, 'origin');
    await ctx.places.nameAll(origins);
    if (origins.length > MAX_BATCH) throw new InputError(`Provide at most ${MAX_BATCH} origins.`);
    const budget = budgetsOf(args)[0];
    const data = await ctx.maps.batch(origins, (o) => ctx.maps.json('/route/range', {
      method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, body: rangeBody({ origin: o, budget, args })
    }));
    const layers = [];
    const markers = [];
    const summary = [];
    (data.batchItems || []).forEach((item, i) => {
      const geometry = polygonOf(item);
      const o = origins[i];
      markers.push({ longitude: o.longitude, latitude: o.latitude, label: labelOf(o, `Origin ${i + 1}`), color: RANGE_COLORS[i % RANGE_COLORS.length] });
      if (!geometry) { summary.push({ origin: labelOf(o, `Origin ${i + 1}`), ok: false }); return; }
      const areaSqKm = round(geometryAreaSqm(geometry) / 1e6, 1);
      summary.push({ origin: labelOf(o, `Origin ${i + 1}`), ok: true, areaSqKm });
      layers.push({ id: `r${i}`, type: 'polygon', name: `${labelOf(o, `Origin ${i + 1}`)} (${areaSqKm} km²)`, geometry: simplifyGeometry(geometry, 120), color: RANGE_COLORS[i % RANGE_COLORS.length], fillOpacity: 0.14 });
    });
    return ok(`Reachable areas within ${budget.label} for ${summary.filter((s) => s.ok).length} of ${origins.length} origin(s):\n${summary.map((s) => `${s.origin}: ${s.ok ? `${s.areaSqKm} km²` : 'none'}`).join('\n')}`, {
      kind: 'map', title: `Reachable within ${budget.label}`, layers, markers, bbox: bboxOf(layers.flatMap((l) => flatten(l.geometry.coordinates)))
    });
  }
}));

// ───────────────────────────── snap to roads ─────────────────────────────

function snapBody(points, args) {
  const body = { type: 'FeatureCollection', features: points.map((p) => point(p.longitude, p.latitude)) };
  const mode = oneOf(args.travelMode, 'travelMode', ['driving', 'truck']);
  if (mode) body.travelMode = mode;
  if (args.interpolate) body.interpolate = true;
  if (args.includeSpeedLimit) body.includeSpeedLimit = true;
  return body;
}

function parseSnap(json, raw) {
  const features = json.features || [];
  const snapped = features.filter((f) => f.geometry && f.geometry.type === 'Point');
  const points = snapped.map((f) => ({ longitude: round(f.geometry.coordinates[0], 6), latitude: round(f.geometry.coordinates[1], 6), kmh: f.properties && f.properties.speedLimitInKilometersPerHour, road: f.properties && f.properties.name, inputIndex: f.properties && f.properties.inputIndex, interpolated: !!(f.properties && f.properties.isInterpolated) }));
  // Interpolation repeats points at road joins, so drop consecutive duplicates.
  const limits = points.filter((p, i) => i === 0 || p.longitude !== points[i - 1].longitude || p.latitude !== points[i - 1].latitude || p.inputIndex !== undefined);
  const line = limits.map((p) => [p.longitude, p.latitude]);
  return { line, points: limits, matched: limits.filter((l) => !l.interpolated).length, total: raw.length };
}

function snapStructured(parsed, raw, title) {
  const rawLine = raw.map((p) => [p.longitude, p.latitude]);
  const speeds = parsed.points.filter((p) => p.kmh !== undefined && p.kmh !== null);
  return {
    kind: 'map', title,
    layers: [
      { id: 'raw', type: 'line', name: 'GPS points', coordinates: rawLine, color: '#8A8886', width: 3, dashed: true },
      { id: 'snapped', type: 'line', name: 'Snapped to roads', coordinates: simplifyLine(parsed.line, 600), color: '#0078D4', width: 5 }
    ],
    markers: raw.map((p, i) => ({ longitude: p.longitude, latitude: p.latitude, label: String(i + 1), color: '#605E5C', small: true })).slice(0, 100),
    ...(speeds.length ? { speedLimits: speeds.slice(0, 300) } : {}),
    bbox: bboxOf([...rawLine, ...parsed.line])
  };
}

tools.push(defineTool({
  name: 'snap_to_roads',
  title: 'Snap GPS points to roads',
  description: 'Match GPS points to the road network and return the matched route, optionally with extra points between them and the speed limit on each road. Give at least 2 points in travel order, up to 5000, with consecutive points within 6 km of each other. Shows the raw GPS trace against the snapped route on a map.',
  properties: {
    points: places('GPS points in travel order, as coordinates.', 2, 5000),
    travelMode: enumOf('Mode of travel. Default driving.', ['driving', 'truck']),
    interpolate: bool('Add points along the road between the snapped points.'),
    includeSpeedLimit: bool('Include speed limits in kilometers per hour.')
  },
  required: ['points'],
  widget: 'map',
  async handler(args, ctx) {
    const raw = await ctx.places.resolveAll(args.points, 'point');
    const json = await ctx.maps.json('/route/snapToRoads', { method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, body: snapBody(raw, args) });
    const parsed = parseSnap(json, raw);
    const roads = [...new Set(parsed.points.map((p) => p.road).filter(Boolean))].slice(0, 12);
    const text = `Snapped ${parsed.matched} of ${parsed.total} point(s) to roads${roads.length ? ` (roads: ${roads.join(', ')})` : ''}.\nSnapped route as [longitude, latitude], ${parsed.line.length} point(s)${parsed.line.length > 40 ? ', evenly sampled to 40' : ''}: ${JSON.stringify(simplifyLine(parsed.line, 40))}${parsed.points.some((p) => p.kmh) ? `\nSpeed limits (km/h): ${jsonText(parsed.points.filter((p) => p.kmh).slice(0, 40).map((p) => ({ road: p.road, kmh: p.kmh }))).text}` : ''}`;
    return ok(text, snapStructured(parsed, raw, `GPS trace snapped to roads (${parsed.matched}/${parsed.total})`));
  }
}));

tools.push(defineTool({
  name: 'snap_to_roads_batch',
  title: 'Snap many GPS traces to roads',
  description: 'Snap up to 100 separate GPS traces to roads in one request. Each trace has at least 2 and up to 100 points.',
  properties: {
    traces: arrayOf('The traces, up to 100.', { type: 'object', properties: { id: str('Your label for the trace.'), points: places('Points in travel order, as coordinates.', 2, 100) } }),
    travelMode: enumOf('Mode of travel. Default driving.', ['driving', 'truck']), interpolate: bool('Add points along the road.'), includeSpeedLimit: bool('Include speed limits.')
  },
  required: ['traces'],
  async handler(args, ctx) {
    const traces = args.traces || [];
    if (!traces.length || traces.length > MAX_BATCH) throw new InputError(`Provide between 1 and ${MAX_BATCH} traces.`);
    const resolved = await Promise.all(traces.map(async (t, i) => ({ id: t.id || String(i + 1), points: await ctx.places.resolveAll(t.points, `trace ${i + 1} point`) })));
    const data = await ctx.maps.batch(resolved, (t) => ctx.maps.json('/route/snapToRoads', {
      method: 'POST', version: VERSIONS.route, accept: GEOJSON, contentType: GEOJSON, body: snapBody(t.points, args)
    }));
    const rows = (data.batchItems || []).map((item, i) => {
      const parsed = parseSnap(item, resolved[i].points);
      return { id: resolved[i].id, matched: parsed.matched, total: parsed.total, snapped: simplifyLine(parsed.line, 100), ...(item.error ? { error: item.error.message } : {}) };
    });
    return dataResult(`Snapped ${rows.length} trace(s).`, rows);
  }
}));

// ───────────────────────────── departure time comparison ─────────────────────────────

const MAX_SLOTS = 24;

async function mapLimit(items, limit, fn) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await fn(items[i], i);
    }
  }));
  return results;
}

tools.push(defineTool({
  name: 'compare_departure_times',
  title: 'Find the best time to leave',
  description: 'Compare how long a trip takes if you leave (or arrive) at different times of a day, using predicted traffic, and find the best and worst times. Accepts addresses or coordinates. Shows a chart of travel time across the day. Times are in the origin\'s local time.',
  properties: {
    origin: place('Where the trip starts.'),
    destination: place('Where the trip ends.'),
    date: str('The day to compare, YYYY-MM-DD, in the origin\'s local time. Default: tomorrow.'),
    startTime: str('First time of the day to test, HH:mm, local time. Default 06:00.'),
    endTime: str('Last time of the day to test, HH:mm, local time. Default 20:00.'),
    stepMinutes: int('Minutes between tests, 15 to 240. Default 60. At most 24 tests are made.', { minimum: 15, maximum: 240 }),
    timeMode: enumOf('Whether the times are departure times (default) or arrival times.', ['departAt', 'arriveAt']),
    travelMode: enumOf('Mode of travel. Default driving.', ['driving', 'truck']),
    avoid: AVOID, vehicle: VEHICLE, units: UNITS
  },
  required: ['origin', 'destination'],
  widget: 'departure',
  async handler(args, ctx) {
    const [origin, destination] = await Promise.all([ctx.places.resolve(args.origin, 'origin'), ctx.places.resolve(args.destination, 'destination')]);
    const { offsetAt } = require('./timezone');

    const tomorrow = new Date(Date.now() + 24 * 3600 * 1000).toISOString().slice(0, 10);
    const date = args.date || tomorrow;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) throw new InputError('date must be YYYY-MM-DD.');
    const parseTime = (value, fallback, name) => {
      const m = /^(\d{1,2}):(\d{2})$/.exec(value || fallback);
      if (!m || Number(m[1]) > 23 || Number(m[2]) > 59) throw new InputError(`${name} must be HH:mm.`);
      return Number(m[1]) * 60 + Number(m[2]);
    };
    const start = parseTime(args.startTime, '06:00', 'startTime');
    const end = parseTime(args.endTime, '20:00', 'endTime');
    if (end < start) throw new InputError('endTime must be after startTime.');
    const step = intInRange(args.stepMinutes, 'stepMinutes', 15, 240, 60);
    const minutes = [];
    for (let m = start; m <= end && minutes.length < MAX_SLOTS; m += step) minutes.push(m);

    const zone = await offsetAt(ctx, origin.longitude, origin.latitude, `${date}T12:00:00Z`);
    const timeMode = args.timeMode === 'arriveAt' ? 'arriveAt' : 'departAt';
    const hhmm = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;

    const slots = await mapLimit(minutes, 4, async (m) => {
      const iso = `${date}T${hhmm(m)}:00${zone.offset}`;
      try {
        const json = await fetchDirections(ctx, { stops: [origin, destination], args: { ...args, optimizeRoute: 'fastestWithTraffic', [timeMode]: iso, departAt: timeMode === 'departAt' ? iso : undefined, arriveAt: timeMode === 'arriveAt' ? iso : undefined }, outputs: ['routeSummary'], maxRouteCount: 1 });
        const r = parseRoute(json);
        if (!r) return { time: hhmm(m), iso, ok: false };
        return { time: hhmm(m), iso, ok: true, seconds: r.trafficSeconds ?? r.seconds, freeFlowSeconds: r.seconds, meters: r.meters, congestion: r.congestion };
      } catch (err) {
        return { time: hhmm(m), iso, ok: false, error: err.message };
      }
    });

    const good = slots.filter((s) => s.ok);
    if (!good.length) throw new InputError(slots.find((s) => s.error)?.error || 'No route was found for those times.');
    const best = good.reduce((a, b) => (b.seconds < a.seconds ? b : a));
    const worst = good.reduce((a, b) => (b.seconds > a.seconds ? b : a));
    const units = args.units || 'metric';
    const verb = timeMode === 'departAt' ? 'leaving' : 'arriving';
    const saved = worst.seconds - best.seconds;
    const text = `${labelOf(origin, 'Origin')} to ${labelOf(destination, 'destination')} on ${date} (${zone.id || 'local time'}, ${good[0] ? formatDistance(good[0].meters, units) : ''}):\n`
      + `Best: ${verb} at ${best.time} takes ${formatDuration(best.seconds)}. Worst: ${verb} at ${worst.time} takes ${formatDuration(worst.seconds)}. Timing it right saves ${formatDuration(saved)}.\n`
      + slots.map((s) => `${s.time}: ${s.ok ? formatDuration(s.seconds) : 'no route'}`).join(' | ')
      + '\nA chart is shown when the client supports it.';
    return ok(text, {
      kind: 'departure', origin: stopSummary(origin), destination: stopSummary(destination), date, timeMode, zone,
      slots, best: { time: best.time, seconds: best.seconds }, worst: { time: worst.time, seconds: worst.seconds }, units, travelMode: args.travelMode || 'driving'
    });
  }
}));

// ───────────────────────────── advanced (Route 1.0) ─────────────────────────────

const V1_MODES = ['car', 'truck', 'taxi', 'bus', 'van', 'motorcycle', 'bicycle', 'pedestrian'];
const V1_ROUTE_TYPES = ['fastest', 'shortest', 'eco', 'thrilling'];
const V1_AVOID = ['tollRoads', 'motorways', 'ferries', 'unpavedRoads', 'carpools', 'alreadyUsedRoads', 'borderCrossings'];
const ENGINE_PROPERTIES = {
  vehicleEngineType: enumOf('Engine type: combustion or electric.', ['combustion', 'electric']),
  constantSpeedConsumptionInLitersPerHundredkm: str('Combustion consumption model, "speed,liters" pairs separated by colons, for example 50,6.3:130,11.5.'),
  currentFuelInLiters: num('Current fuel in liters.'), auxiliaryPowerInLitersPerHour: num('Fuel used by auxiliary systems per hour.'),
  fuelEnergyDensityInMJoulesPerLiter: num('Fuel energy density in MJ per liter.'),
  accelerationEfficiency: num('Acceleration efficiency.'), decelerationEfficiency: num('Deceleration efficiency.'),
  uphillEfficiency: num('Uphill efficiency.'), downhillEfficiency: num('Downhill efficiency.'),
  constantSpeedConsumptionInkWhPerHundredkm: str('Electric consumption model, "speed,kWh" pairs separated by colons, for example 50,8.2:130,21.3.'),
  currentChargeInkWh: num('Current battery charge in kWh.'), maxChargeInkWh: num('Maximum battery charge in kWh.'),
  auxiliaryPowerInkW: num('Power used by auxiliary systems in kW.'),
  vehicleWeight: int('Vehicle weight in kg.'), vehicleWidth: num('Vehicle width in meters.'), vehicleHeight: num('Vehicle height in meters.'),
  vehicleLength: num('Vehicle length in meters.'), vehicleAxleWeight: int('Axle weight in kg.'), vehicleMaxSpeed: int('Maximum speed in km/h.'),
  vehicleCommercial: bool('Commercial use.'), vehicleLoadType: strings('Hazardous load classes, for example USHazmatClass1.')
};
const ENGINE_KEYS = Object.keys(ENGINE_PROPERTIES);

tools.push(defineTool({
  name: 'get_route_directions_advanced',
  title: 'Get directions with vehicle, EV and route-type options',
  description: 'Directions using the Route 1.0 engine, which supports more modes and options than get_route_directions: car, truck, taxi, bus, van, motorcycle, bicycle and pedestrian; fastest, shortest, eco and thrilling routes; electric and combustion consumption; best waypoint order; and route sections such as traffic, toll roads and tunnels. Use it when you need one of those; otherwise use get_route_directions.',
  properties: {
    origin: place('Start.'), destination: place('End.'), waypoints: places('Optional stops in order.'),
    travelMode: enumOf('Mode. Default car.', V1_MODES), routeType: enumOf('Route type. Default fastest.', V1_ROUTE_TYPES),
    avoid: strings(`Features to avoid: ${V1_AVOID.join(', ')}.`), traffic: bool('Use live traffic. Default true.'),
    departAt: DEPART_AT, arriveAt: ARRIVE_AT, maxAlternatives: int('Alternative routes, 0 to 5.', { minimum: 0, maximum: 5 }),
    computeBestOrder: bool('Reorder waypoints for the shortest total route.'),
    sectionType: strings('Sections to return: carTrain, country, ferry, motorway, pedestrian, tollRoad, tollVignette, traffic, travelMode, tunnel, carpool, urban.'),
    instructionsType: enumOf('Instruction format. Default none; text returns plain text steps.', ['coded', 'text', 'tagged']),
    hilliness: enumOf('For thrilling routes: hilliness.', ['low', 'normal', 'high']), windingness: enumOf('For thrilling routes: windingness.', ['low', 'normal', 'high']),
    ...ENGINE_PROPERTIES, units: UNITS, language: LANGUAGE
  },
  required: ['origin', 'destination'],
  widget: 'route',
  async handler(args, ctx) {
    const stops = await resolveStops(args, ctx);
    const q = {
      query: stops.map((s) => `${s.latitude},${s.longitude}`).join(':'),
      travelMode: args.travelMode, routeType: args.routeType, avoid: list(args.avoid), traffic: args.traffic, departAt: args.departAt, arriveAt: args.arriveAt,
      maxAlternatives: args.maxAlternatives, computeBestOrder: args.computeBestOrder, sectionType: list(args.sectionType), instructionsType: args.instructionsType || 'text',
      hilliness: args.hilliness, windingness: args.windingness, language: args.language, routeRepresentation: 'polyline', computeTravelTimeFor: 'all'
    };
    for (const k of ENGINE_KEYS) if (args[k] !== undefined) q[k] = Array.isArray(args[k]) ? args[k].join(',') : args[k];
    const data = await ctx.maps.json('/route/directions/json', { version: VERSIONS.routeV1, query: q });

    const units = args.units || 'metric';
    const routes = (data.routes || []).map((r, index) => {
      const s = r.summary || {};
      const full = (r.legs || []).flatMap((l) => (l.points || []).map((p) => [p.longitude, p.latitude]));
      const steps = ((r.guidance && r.guidance.instructions) || []).map((i) => ({ text: i.message || i.street || i.instructionType || '', meters: Math.round(i.routeOffsetInMeters || 0), seconds: Math.round(i.travelTimeInSeconds || 0), longitude: i.point && round(i.point.longitude, 5), latitude: i.point && round(i.point.latitude, 5), maneuver: i.maneuver || '' }));
      return {
        index, meters: s.lengthInMeters, seconds: s.travelTimeInSeconds,
        departAt: s.departureTime, arriveAt: s.arrivalTime, fuelLiters: s.fuelConsumptionInLiters, batteryKwh: s.batteryConsumptionInkWh,
        line: roundLine(simplifyLine(full, 400)), steps: steps.slice(0, 150), stepCount: steps.length,
        sections: (r.sections || []).slice(0, 80).map((x) => ({ type: x.sectionType, travelMode: x.travelMode, startPointIndex: x.startPointIndex, endPointIndex: x.endPointIndex, simpleCategory: x.simpleCategory, delayInSeconds: x.delayInSeconds, magnitudeOfDelay: x.magnitudeOfDelay }))
      };
    }).filter((r) => r.line.length);
    if (!routes.length) throw new InputError('No route was found.');
    const lines = routes.map((r, i) => `Route ${i + 1}: ${formatDistance(r.meters, units)}, ${formatDuration(r.seconds)}${r.fuelLiters !== undefined ? `, fuel ${r.fuelLiters} L` : ''}${r.batteryKwh !== undefined ? `, battery ${r.batteryKwh} kWh` : ''}`);
    const stepLines = routes[0].steps.slice(0, 60).map((s, i) => `${i + 1}. ${s.text}`);
    return ok(`Advanced directions from ${labelOf(stops[0])} to ${labelOf(stops[stops.length - 1])}:\n${lines.join('\n')}\n\nSteps for route 1:\n${stepLines.join('\n')}\nAn interactive map shows the routes when the client supports it.`, {
      kind: 'route', stops: stops.map(stopSummary), routes, request: { travelMode: args.travelMode || 'car', units, readOnly: true }, bingUrl: bingRouteUrl(stops, args.travelMode === 'pedestrian' ? 'walking' : 'driving')
    });
  }
}));

tools.push(defineTool({
  name: 'get_reachable_range_advanced',
  title: 'Get the range of an electric or combustion vehicle',
  description: 'Get the area a vehicle can reach with the fuel or battery it has, or within a time or distance budget, using the Route 1.0 engine. Use this for electric vehicle range ("how far can I go on 40 kWh") and for vehicle types such as bus, van and motorcycle. Shows the area on an interactive map.',
  properties: {
    origin: place('Starting point.'),
    fuelBudgetInLiters: num('Fuel budget in liters (combustion engines).'), energyBudgetInkWh: num('Energy budget in kWh (electric engines).'),
    timeBudgetInSec: num('Time budget in seconds.'), distanceBudgetInMeters: num('Distance budget in meters.'),
    travelMode: enumOf('Mode. Default car.', ['car', 'truck', 'taxi', 'bus', 'van', 'motorcycle']), routeType: enumOf('Route type.', V1_ROUTE_TYPES),
    traffic: bool('Use live traffic.'), departAt: DEPART_AT, avoid: strings(`Features to avoid: ${V1_AVOID.join(', ')}.`),
    hilliness: enumOf('Hilliness for thrilling routes.', ['low', 'normal', 'high']), windingness: enumOf('Windingness for thrilling routes.', ['low', 'normal', 'high']),
    ...ENGINE_PROPERTIES, units: UNITS
  },
  required: ['origin'],
  widget: 'reach',
  async handler(args, ctx) {
    const origin = await ctx.places.resolve(args.origin, 'origin');
    const budgetKeys = ['fuelBudgetInLiters', 'energyBudgetInkWh', 'timeBudgetInSec', 'distanceBudgetInMeters'];
    if (!budgetKeys.some((k) => args[k])) throw new InputError('Provide one budget: fuelBudgetInLiters, energyBudgetInkWh, timeBudgetInSec or distanceBudgetInMeters.');
    const q = { query: [origin.latitude, origin.longitude], travelMode: args.travelMode, routeType: args.routeType, traffic: args.traffic, departAt: args.departAt, avoid: list(args.avoid), hilliness: args.hilliness, windingness: args.windingness };
    for (const k of [...budgetKeys, ...ENGINE_KEYS]) if (args[k] !== undefined) q[k] = Array.isArray(args[k]) ? args[k].join(',') : args[k];
    const data = await ctx.maps.json('/route/range/json', { version: VERSIONS.routeV1, query: q });
    const boundary = ((data.reachableRange || {}).boundary || []).map((p) => [p.longitude, p.latitude]);
    if (boundary.length < 3) throw new InputError('No reachable area was returned.');
    const ring = [...boundary, boundary[0]];
    const geometry = simplifyGeometry({ type: 'Polygon', coordinates: [ring] }, 200);
    const areaSqKm = round(geometryAreaSqm({ type: 'Polygon', coordinates: [ring] }) / 1e6, 1);
    const budget = args.energyBudgetInkWh ? `${args.energyBudgetInkWh} kWh` : args.fuelBudgetInLiters ? `${args.fuelBudgetInLiters} L` : args.timeBudgetInSec ? formatDuration(args.timeBudgetInSec) : formatDistance(args.distanceBudgetInMeters, args.units);
    return ok(`Reachable area from ${labelOf(origin, 'the origin')} within ${budget}: about ${areaSqKm} square kilometers.\nAn interactive map shows the area when the client supports it.`, {
      kind: 'reach', origin: { longitude: origin.longitude, latitude: origin.latitude, name: labelOf(origin, 'Start') },
      bands: [{ type: 'budget', label: budget, color: RANGE_COLORS[1], geometry, areaSqKm }], request: { travelMode: args.travelMode || 'car', units: args.units || 'metric', readOnly: true }, bbox: bboxOf(flatten(geometry.coordinates))
    });
  }
}));

module.exports = { tools, fetchDirections, parseRoute, parseRoutes, resolveStops, mapLimit, bingRouteUrl, TRAVEL_MODES, AVOID_VALUES, haversine, parsePlace };
