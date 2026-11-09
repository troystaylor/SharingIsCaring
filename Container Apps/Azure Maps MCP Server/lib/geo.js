'use strict';

const EARTH_RADIUS_M = 6371008.8;
const rad = (d) => (d * Math.PI) / 180;

const round = (n, places = 5) => {
  const f = 10 ** places;
  return Math.round(n * f) / f;
};

function haversine(a, b) {
  const dLat = rad(b[1] - a[1]);
  const dLon = rad(b[0] - a[0]);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a[1])) * Math.cos(rad(b[1])) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_RADIUS_M * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** Flattens any GeoJSON coordinate nesting into a list of [lon, lat] positions. */
function flatten(token, out = []) {
  if (!Array.isArray(token) || token.length === 0) return out;
  if (typeof token[0] === 'number') {
    if (token.length >= 2) out.push([token[0], token[1]]);
    return out;
  }
  for (const child of token) flatten(child, out);
  return out;
}

function bboxOf(positions) {
  if (!positions.length) return null;
  let minLon = Infinity, minLat = Infinity, maxLon = -Infinity, maxLat = -Infinity;
  for (const [lon, lat] of positions) {
    if (lon < minLon) minLon = lon;
    if (lon > maxLon) maxLon = lon;
    if (lat < minLat) minLat = lat;
    if (lat > maxLat) maxLat = lat;
  }
  return [minLon, minLat, maxLon, maxLat];
}

function distanceToSegment(p, a, b, lonScale) {
  const ax = a[0] * lonScale, ay = a[1], bx = b[0] * lonScale, by = b[1], px = p[0] * lonScale, py = p[1];
  const dx = bx - ax, dy = by - ay;
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
  return Math.hypot(ax + t * dx - px, ay + t * dy - py);
}

/** Reduces a line to at most maxPoints, keeping the points that matter most for its shape and both ends. */
function simplifyLine(line, maxPoints) {
  if (line.length <= maxPoints) return line;
  const keep = Math.max(2, maxPoints);
  const lonScale = Math.cos((line.reduce((s, p) => s + p[1], 0) / line.length) * Math.PI / 180);
  const importance = new Array(line.length).fill(0);
  importance[0] = importance[line.length - 1] = Infinity;
  const stack = [[0, line.length - 1]];
  while (stack.length) {
    const [first, last] = stack.pop();
    if (last - first < 2) continue;
    let far = first + 1, farDistance = -1;
    for (let i = first + 1; i < last; i++) {
      const d = distanceToSegment(line[i], line[first], line[last], lonScale);
      if (d > farDistance) { farDistance = d; far = i; }
    }
    importance[far] = farDistance;
    stack.push([first, far], [far, last]);
  }
  return importance
    .map((value, index) => ({ value, index }))
    .sort((a, b) => (b.value - a.value) || (a.index - b.index))
    .slice(0, keep)
    .sort((a, b) => a.index - b.index)
    .map((x) => line[x.index]);
}

const roundLine = (line, places = 5) => line.map(([lon, lat]) => [round(lon, places), round(lat, places)]);

/** Approximate area of a polygon ring in square meters (equirectangular projection around its centroid). */
function ringAreaSqm(ring) {
  if (ring.length < 4) return 0;
  const lat0 = ring.reduce((s, p) => s + p[1], 0) / ring.length;
  const kx = rad(1) * EARTH_RADIUS_M * Math.cos(rad(lat0));
  const ky = rad(1) * EARTH_RADIUS_M;
  let sum = 0;
  for (let i = 0; i < ring.length - 1; i++) {
    sum += ring[i][0] * kx * ring[i + 1][1] * ky - ring[i + 1][0] * kx * ring[i][1] * ky;
  }
  return Math.abs(sum) / 2;
}

/** Area of a Polygon, MultiPolygon or GeometryCollection geometry in square meters, holes subtracted. */
function geometryAreaSqm(geometry) {
  let total = 0;
  for (const rings of polygonsOf(geometry)) {
    rings.forEach((ring, i) => { total += (i === 0 ? 1 : -1) * ringAreaSqm(ring); });
  }
  return Math.max(0, total);
}

function polygonsOf(geometry) {
  if (!geometry) return [];
  if (geometry.type === 'Polygon') return [geometry.coordinates];
  if (geometry.type === 'MultiPolygon') return geometry.coordinates;
  if (geometry.type === 'GeometryCollection') return (geometry.geometries || []).flatMap(polygonsOf);
  return [];
}

/** Reduces every ring of a polygon geometry to roughly maxPoints in total, so results stay small. */
function simplifyGeometry(geometry, maxPointsPerRing) {
  const polygons = polygonsOf(geometry);
  return {
    type: 'MultiPolygon',
    coordinates: polygons.map((rings) => rings.map((ring) => {
      const simple = roundLine(simplifyLine(ring, maxPointsPerRing));
      const first = simple[0], last = simple[simple.length - 1];
      if (first && last && (first[0] !== last[0] || first[1] !== last[1])) simple.push(first);
      return simple;
    }))
  };
}

function formatDuration(seconds) {
  const s = Math.max(0, Math.round(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.round((s % 3600) / 60);
  if (h >= 24) { const d = Math.floor(h / 24); return `${d} d ${h % 24} h`; }
  if (h > 0) return `${h} h ${m} min`;
  return `${Math.max(1, m)} min`;
}

function formatDistance(meters, units = 'metric') {
  if (units === 'imperial') {
    const miles = meters / 1609.344;
    return miles >= 0.2 ? `${miles >= 10 ? Math.round(miles) : miles.toFixed(1)} mi` : `${Math.round(meters * 3.28084)} ft`;
  }
  return meters >= 1000 ? `${meters >= 10000 ? Math.round(meters / 1000) : (meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`;
}

module.exports = {
  EARTH_RADIUS_M, haversine, flatten, bboxOf, simplifyLine, roundLine, round,
  ringAreaSqm, geometryAreaSqm, polygonsOf, simplifyGeometry, formatDuration, formatDistance
};
