/**
 * Map — port of ext-apps map-server.
 * Interactive map using OpenStreetMap tiles (free, no API key).
 * Uses Leaflet.js (lightweight ~40KB) instead of CesiumJS (~2MB) for Cowork compatibility.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerMap(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/map.html";

  server.resource("Map UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(mapWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_map",
    {
      description: "Display an interactive map centered on a location. Uses OpenStreetMap tiles (free, no API key).",
      inputSchema: {
        query: z.string().optional().describe("Place name or address to center on (default: 'Seattle')"),
        latitude: z.number().optional().describe("Latitude (overrides query)"),
        longitude: z.number().optional().describe("Longitude (overrides query)"),
        zoom: z.number().min(1).max(18).optional().describe("Zoom level 1-18 (default 12)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      let lat = args.latitude;
      let lon = args.longitude;
      let name = args.query || "Seattle";

      if (lat === undefined || lon === undefined) {
        // Geocode using OpenStreetMap Nominatim (free, no key)
        const url = `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(name)}&limit=1`;
        const res = await fetch(url, { headers: { "User-Agent": "MCP-Apps-Demo/1.0" } });
        if (res.ok) {
          const results = await res.json() as Array<{ lat: string; lon: string; display_name: string }>;
          if (results.length > 0) {
            lat = parseFloat(results[0].lat);
            lon = parseFloat(results[0].lon);
            name = results[0].display_name;
          }
        }
      }

      lat = lat ?? 47.6062;
      lon = lon ?? -122.3321;
      const zoom = args.zoom || 12;

      return {
        content: [{ type: "text" as const, text: `Map: ${name} (${lat.toFixed(4)}, ${lon.toFixed(4)}) at zoom ${zoom}` }],
        structuredContent: { latitude: lat, longitude: lon, zoom, name },
      };
    }
  );
}

function mapWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Map</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; }
  #map { width:100%; height:100vh; }
  .info { position:absolute; top:10px; left:50px; background:rgba(255,255,255,0.9); padding:8px 12px;
    border-radius:6px; font-size:12px; color:#333; z-index:1000; box-shadow:0 1px 4px rgba(0,0,0,0.2); }
</style>
<!-- Leaflet CSS + JS inlined as data URIs would be too large; use CDN with CSP note -->
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"><\/script>
</head>
<body>
<div class="info" id="info">Loading map...</div>
<div id="map"></div>
<script>
(function() {
  let map = null;
  function render(data) {
    document.getElementById("info").textContent = data.name || "Map";
    if (!map) {
      map = L.map("map").setView([data.latitude, data.longitude], data.zoom || 12);
      L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "&copy; OpenStreetMap contributors",
        maxZoom: 19,
      }).addTo(map);
    } else {
      map.setView([data.latitude, data.longitude], data.zoom || 12);
    }
    L.marker([data.latitude, data.longitude]).addTo(map)
      .bindPopup(data.name || "Location").openPopup();
  }
  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
