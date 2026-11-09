'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult } = require('../define');
const { str, num, int, bool, enumOf, strings, place } = require('../schema');
const { InputError, intInRange, oneOf, list, labelOf } = require('../args');
const { round, bboxOf, haversine, formatDistance } = require('../geo');

const LOCATION = place('The center of the area. Accepts an address, a place name or coordinates.');

const INCIDENT_TYPES = ['Accident', 'Congestion', 'DisabledVehicle', 'Miscellaneous', 'RoadHazard', 'Construction', 'Weather'];
const MAX_BBOX_SQKM = 10000;

/** A bounding box around a point, widened to the requested radius in kilometers. */
function boxAround(lon, lat, radiusKm) {
  const dLat = radiusKm / 110.574;
  const dLon = radiusKm / (111.32 * Math.max(0.01, Math.cos((lat * Math.PI) / 180)));
  return [lon - dLon, lat - dLat, lon + dLon, lat + dLat].map((n) => round(n, 5));
}

function boxAreaSqKm(b) {
  const w = haversine([b[0], (b[1] + b[3]) / 2], [b[2], (b[1] + b[3]) / 2]);
  const h = haversine([(b[0] + b[2]) / 2, b[1]], [(b[0] + b[2]) / 2, b[3]]);
  return (w * h) / 1e6;
}

function parseBox(value) {
  const parts = Array.isArray(value) ? value.map(Number) : String(value).split(',').map((s) => Number(s.trim()));
  if (parts.length !== 4 || parts.some((n) => !Number.isFinite(n))) throw new InputError('bbox must be minLongitude,minLatitude,maxLongitude,maxLatitude.');
  return parts;
}

// Web Mercator meters, used by the legacy incident endpoints.
const mercatorX = (lon) => (lon * 20037508.34) / 180;
const mercatorY = (lat) => (Math.log(Math.tan(((90 + lat) * Math.PI) / 360)) / (Math.PI / 180)) * 20037508.34 / 180;

function normalizeIncident(f) {
  const p = f.properties || {};
  const g = f.geometry || {};
  const end = p.endPoint && p.endPoint.coordinates;
  return Object.fromEntries(Object.entries({
    id: f.id !== undefined ? String(f.id) : undefined,
    type: p.incidentType, title: p.title, description: p.description, severity: p.severity, roadClosed: p.isRoadClosed || undefined,
    trafficJam: p.isTrafficJam || undefined, delaySeconds: p.delay || undefined, start: p.startTime, end: p.endTime,
    longitude: g.coordinates ? round(g.coordinates[0], 5) : undefined, latitude: g.coordinates ? round(g.coordinates[1], 5) : undefined,
    endLongitude: end ? round(end[0], 5) : undefined, endLatitude: end ? round(end[1], 5) : undefined
  }).filter(([, v]) => v !== undefined && v !== null));
}

const tools = [];

tools.push(defineTool({
  name: 'get_traffic_incidents',
  title: 'Get traffic incidents',
  description: 'List traffic incidents such as accidents, congestion, road closures, construction, road hazards and weather around a place or inside a bounding box (up to 10,000 square kilometers). Shows them on a live traffic map with filters, a list sorted by severity, and the current speed on any road the user taps.',
  properties: {
    location: place('The center of the area to check. Use with radiusKm.'),
    radiusKm: num('Half the width of the area in kilometers. Default 10. The area must be under 10,000 square kilometers.'),
    bbox: str('Alternative to location: minLongitude,minLatitude,maxLongitude,maxLatitude.'),
    incidentTypes: strings(`Only these types: ${INCIDENT_TYPES.join(', ')}. Default all.`)
  },
  widget: 'traffic',
  async handler(args, ctx) {
    let box, center;
    if (args.bbox) {
      box = parseBox(args.bbox);
    } else if (args.location) {
      center = await ctx.places.resolve(args.location, 'location');
      box = boxAround(center.longitude, center.latitude, args.radiusKm || 10);
    } else {
      throw new InputError('Provide location or bbox.');
    }
    if (boxAreaSqKm(box) > MAX_BBOX_SQKM) throw new InputError('The area is too large. It must be under 10,000 square kilometers (about 100 km by 100 km).');
    const types = list(args.incidentTypes).map((t) => oneOf(t, 'incidentTypes', INCIDENT_TYPES));
    const data = await ctx.maps.json('/traffic/incident', { version: VERSIONS.traffic, accept: 'application/geo+json', query: { bbox: box, incidentType: types } });
    const incidents = (data.features || []).map(normalizeIncident).sort((a, b) => (b.severity ?? 0) - (a.severity ?? 0) || (b.delaySeconds ?? 0) - (a.delaySeconds ?? 0));
    const counts = {};
    for (const i of incidents) counts[i.type || 'Other'] = (counts[i.type || 'Other'] || 0) + 1;
    const closures = incidents.filter((i) => i.roadClosed).length;
    const top = incidents.slice(0, 15).map((i, n) => `${n + 1}. ${i.type}${i.title ? ` on ${i.title}` : ''}${i.description ? ` — ${i.description}` : ''}${i.roadClosed ? ' (road closed)' : ''}${i.delaySeconds ? ` (+${Math.round(i.delaySeconds / 60)} min delay)` : ''}`);
    const c = center || { longitude: (box[0] + box[2]) / 2, latitude: (box[1] + box[3]) / 2 };
    return ok(`${incidents.length} traffic incident(s) in the area${center ? ` around ${labelOf(center)}` : ''}: ${Object.entries(counts).map(([k, v]) => `${v} ${k}`).join(', ') || 'none'}${closures ? `, ${closures} road closure(s)` : ''}.\n${top.join('\n')}\nA traffic map is shown when the client supports it.`, {
      kind: 'traffic', bbox: box, center: { longitude: c.longitude, latitude: c.latitude, name: center ? labelOf(center) : undefined }, incidents: incidents.slice(0, 300), counts
    });
  }
}));

tools.push(defineTool({
  name: 'get_traffic_flow_segment',
  title: 'Get the current traffic speed on a road',
  description: 'Get the current speed and travel time on the road segment closest to a point, compared with free-flow conditions, so you can tell how congested it is. Use zoom to choose which road classes count: low zoom for highways, high zoom for local streets.',
  properties: {
    location: place('A point on or near the road.'),
    zoom: int('Zoom level 0 to 22 that selects the road class. Default 12.', { minimum: 0, maximum: 22 }),
    style: enumOf('absolute (default), relative or relative-delay.', ['absolute', 'relative', 'relative-delay']),
    unit: enumOf('Speed unit. Default KMPH.', ['KMPH', 'MPH']),
    openLr: bool('Include the OpenLR location reference of the segment.')
  },
  required: ['location'],
  widget: 'traffic',
  async handler(args, ctx) {
    const at = await ctx.places.resolve(args.location, 'location');
    const data = await ctx.maps.json('/traffic/flow/segment/json', {
      version: VERSIONS.trafficV1, query: { style: args.style || 'absolute', zoom: intInRange(args.zoom, 'zoom', 0, 22, 12), query: [at.latitude, at.longitude], unit: args.unit || 'KMPH', openLr: args.openLr }
    });
    const s = data.flowSegmentData;
    if (!s) throw new InputError('No traffic data is available near that point.');
    const coordinates = ((s.coordinates && s.coordinates.coordinate) || []).map((c) => [round(c.longitude, 5), round(c.latitude, 5)]);
    const ratio = s.freeFlowSpeed ? s.currentSpeed / s.freeFlowSpeed : 1;
    const level = s.roadClosure ? 'closed' : ratio >= 0.85 ? 'free flowing' : ratio >= 0.6 ? 'moderate' : ratio >= 0.35 ? 'heavy' : 'standstill';
    const unit = args.unit === 'MPH' ? 'mph' : 'km/h';
    const flow = { currentSpeed: s.currentSpeed, freeFlowSpeed: s.freeFlowSpeed, currentTravelTime: s.currentTravelTime, freeFlowTravelTime: s.freeFlowTravelTime, confidence: s.confidence, roadClosure: !!s.roadClosure, roadClass: s.frc, unit, level, line: coordinates, ...(s.openlr ? { openLr: s.openlr } : {}) };
    return ok(`Traffic near ${labelOf(at)}: ${level}. Current speed ${s.currentSpeed} ${unit} versus ${s.freeFlowSpeed} ${unit} free flow (${Math.round(ratio * 100)}%), travel time ${s.currentTravelTime}s versus ${s.freeFlowTravelTime}s. Confidence ${s.confidence}.`, {
      kind: 'trafficFlow', center: { longitude: at.longitude, latitude: at.latitude, name: labelOf(at) }, flow
    });
  }
}));

tools.push(defineTool({
  name: 'get_traffic_incident_detail',
  title: 'Get detailed traffic incident data (legacy)',
  description: 'Get detailed incident records from the legacy Traffic 1.0 incident service for a bounding box, including clusters and original positions. get_traffic_incidents is simpler and newer; use this when you need the legacy fields. The viewport call that Azure Maps requires first is made for you.',
  properties: {
    bbox: str('Area as minLongitude,minLatitude,maxLongitude,maxLatitude.'),
    zoom: int('Zoom level 0 to 22 of the view. Default 11.', { minimum: 0, maximum: 22 }),
    style: enumOf('Incident icon style. Default s3.', ['s1', 's2', 's3', 'night']),
    language: str('Language as an ISO 639-1 code, for example en.'),
    geometries: enumOf('Vector geometry type: original or shifted.', ['original', 'shifted']),
    expandCluster: bool('List every incident in a cluster separately.'),
    originalPosition: bool('Also return the original position of each incident.')
  },
  required: ['bbox'],
  async handler(args, ctx) {
    const b = parseBox(args.bbox);
    const zoom = intInRange(args.zoom, 'zoom', 0, 22, 11);
    const merc = [mercatorX(b[0]), mercatorY(b[1]), mercatorX(b[2]), mercatorY(b[3])].map(Math.round);
    const viewport = await ctx.maps.json('/traffic/incident/viewport/json', {
      version: VERSIONS.trafficV1, query: { boundingbox: merc, boundingzoom: zoom, overviewbox: merc, overviewzoom: zoom, copyright: false }
    });
    const modelId = viewport.viewpResp && viewport.viewpResp.trafficState && viewport.viewpResp.trafficState['@trafficModelId'];
    if (!modelId) throw new InputError('Azure Maps did not return a traffic model for that area.');
    const data = await ctx.maps.json('/traffic/incident/detail/json', {
      version: VERSIONS.trafficV1,
      query: { style: args.style || 's3', boundingbox: [b[1], b[0], b[3], b[2]], boundingZoom: zoom, trafficmodelid: modelId, projection: 'EPSG4326', language: args.language, geometries: args.geometries, expandCluster: args.expandCluster, originalPosition: args.originalPosition }
    });
    const poi = (data.tm && data.tm.poi) || [];
    return dataResult(`${poi.length} legacy traffic record(s) for the area (traffic model ${modelId}).`, data.tm || data);
  }
}));

module.exports = { tools, boxAround, normalizeIncident };
