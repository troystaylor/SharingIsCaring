'use strict';

const fs = require('fs');
const path = require('path');

const DIR = path.join(__dirname, '..', 'widgets');
const WIDGET_MIME = 'text/html;profile=mcp-app';

const WIDGETS = {
  map: { name: 'Interactive map', description: 'A map you can pan and zoom, with pins, lines, areas, live traffic and weather radar.' },
  places: { name: 'Place finder', description: 'Search results on a map with a list, and a search box with suggestions.' },
  route: { name: 'Route planner', description: 'Routes with alternatives, travel times and turn-by-turn directions on an interactive map.' },
  reach: { name: 'Reachable area', description: 'Areas reachable within a time or distance, with places inside them.' },
  matrix: { name: 'Travel time matrix', description: 'A heat-map table of travel times between origins and destinations.' },
  departure: { name: 'Best time to leave', description: 'A chart of travel time across the day, with the best and worst times to leave.' },
  weather: { name: 'Weather dashboard', description: 'Current conditions, hourly and daily forecast, alerts and air quality.' },
  routeweather: { name: 'Weather along the route', description: 'The route colored by weather hazard, with a timeline of conditions along the way.' },
  traffic: { name: 'Traffic', description: 'Traffic incidents and road speeds on a live map.' },
  clock: { name: 'World clock and meeting planner', description: 'Local times, daylight and the hours when everyone is in working hours.' },
  batch: { name: 'Batch geocoding', description: 'A table of geocoding results with match quality, a map and a CSV download.' },
  storms: { name: 'Storm tracker', description: 'Tropical storms with their past and forecast tracks.' }
};

const read = (file) => fs.readFileSync(path.join(DIR, file), 'utf8');

/**
 * Builds each widget page by inserting the shared styles and script into its template. Pages load nothing from the
 * network: map tiles arrive as data: URLs through the host, which calls the server's tools on the widget's behalf.
 */
function build() {
  const css = read('shared.css');
  const js = ['bridge.js', 'ui.js', 'map-engine.js'].map(read).join('\n');
  const out = {};
  for (const [id, info] of Object.entries(WIDGETS)) {
    const file = path.join(DIR, `${id}.html`);
    if (!fs.existsSync(file)) continue;
    const html = read(`${id}.html`)
      .replace('/*@shared-css*/', () => css)
      .replace('/*@shared-js*/', () => js);
    out[`ui://azure-maps/${id}.html`] = { ...info, html };
  }
  return out;
}

const RESOURCES = build();

/** Rebuilds the pages from disk. The local preview calls this so edits show up without a restart. */
function reload() {
  for (const key of Object.keys(RESOURCES)) delete RESOURCES[key];
  Object.assign(RESOURCES, build());
}

const resourceList = () => Object.entries(RESOURCES).map(([uri, w]) => ({ uri, name: w.name, description: w.description, mimeType: WIDGET_MIME }));

const resourceContent = (uri) => ({ uri, mimeType: WIDGET_MIME, text: RESOURCES[uri].html, _meta: { ui: { prefersBorder: false } } });

module.exports = { RESOURCES, resourceList, resourceContent, reload, WIDGET_MIME };
