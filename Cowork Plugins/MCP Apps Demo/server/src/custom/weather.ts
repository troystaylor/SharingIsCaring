/**
 * Weather Forecast — demo data with city-specific variations.
 * Tools: show_weather (model+app), refresh_weather (app-only)
 * Uses elicitation to collect city and forecast days.
 * NOTE: Uses generated demo data (not a live API) because Container Apps
 * outbound HTTPS to Open-Meteo is blocked in this environment.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { tryElicitate } from "../shared/elicitation.js";
import { injectBootstrap } from "../shared/widget-bootstrap.js";
import { weatherWidgetHtml } from "./widgets/weather-widget.js";

// City data for demo — deterministic based on city name
const CITIES: Record<string, { lat: number; lon: number; country: string; baseTemp: number }> = {
  seattle: { lat: 47.61, lon: -122.33, country: "United States", baseTemp: 18 },
  "los angeles": { lat: 34.05, lon: -118.24, country: "United States", baseTemp: 28 },
  "new york": { lat: 40.71, lon: -74.01, country: "United States", baseTemp: 24 },
  london: { lat: 51.51, lon: -0.13, country: "United Kingdom", baseTemp: 16 },
  tokyo: { lat: 35.68, lon: 139.69, country: "Japan", baseTemp: 22 },
  paris: { lat: 48.86, lon: 2.35, country: "France", baseTemp: 19 },
  sydney: { lat: -33.87, lon: 151.21, country: "Australia", baseTemp: 14 },
  dubai: { lat: 25.20, lon: 55.27, country: "UAE", baseTemp: 38 },
  berlin: { lat: 52.52, lon: 13.40, country: "Germany", baseTemp: 17 },
  mumbai: { lat: 19.08, lon: 72.88, country: "India", baseTemp: 32 },
};

const CONDITIONS = ["Clear sky", "Mainly clear", "Partly cloudy", "Slight rain", "Overcast", "Sunny", "Light showers"];

function generateForecast(city: string, days: number) {
  const key = city.toLowerCase();
  const cityInfo = CITIES[key] || { lat: 40.0, lon: -100.0, country: "Unknown", baseTemp: 20 };
  const cityName = Object.keys(CITIES).find(k => k === key)
    ? city.charAt(0).toUpperCase() + city.slice(1).replace(/(^|\s)\w/g, c => c.toUpperCase())
    : city;

  // Deterministic seed from city name
  let seed = 0;
  for (let i = 0; i < city.length; i++) seed = (seed * 31 + city.charCodeAt(i)) & 0x7fffffff;
  const rand = (i: number) => ((seed * (i + 7) * 13) % 100) / 100;

  const today = new Date();
  const daily = Array.from({ length: days }, (_, i) => {
    const date = new Date(today);
    date.setDate(date.getDate() + i);
    const variation = Math.round((rand(i) - 0.5) * 8);
    const tempMax = cityInfo.baseTemp + variation + 3;
    const tempMin = cityInfo.baseTemp + variation - 5;
    return {
      date: date.toISOString().split("T")[0],
      tempMax,
      tempMin,
      precipitation: rand(i + 100) > 0.7 ? Math.round(rand(i + 200) * 15 * 10) / 10 : 0,
      condition: CONDITIONS[Math.floor(rand(i + 50) * CONDITIONS.length)],
    };
  });

  return {
    city: cityName,
    country: cityInfo.country,
    latitude: cityInfo.lat,
    longitude: cityInfo.lon,
    loading: false,
    current: {
      temperature: daily[0].tempMax - 2,
      humidity: Math.round(40 + rand(999) * 40),
      windSpeed: Math.round(5 + rand(888) * 20),
      condition: daily[0].condition,
    },
    daily,
  };
}

export function registerWeather(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/weather.html";

  server.resource("Weather Forecast UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(weatherWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_weather",
    {
      description: "Display a weather forecast widget for any city. Shows current conditions, temperature chart, and daily forecast cards.",
      inputSchema: {
        city: z.string().optional().describe("City name (e.g., 'Seattle')"),
        days: z.number().min(1).max(14).optional().describe("Forecast days (1-14, default 7)"),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const elicited = await tryElicitate<{ city: string; days: number }>(
        server, "Enter the location for the weather forecast.", {
          type: "object",
          properties: {
            city: { type: "string", title: "City", description: "City name (e.g., Seattle, London, Tokyo)" },
            days: { type: "integer" as any, title: "Forecast Days", description: "Number of days (1-14)", default: 7 },
          },
          required: ["city"],
        }
      );

      const city = elicited?.city || args.city || "Seattle";
      const days = elicited?.days || args.days || 7;
      const data = generateForecast(city, days);

      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `Weather for ${data.city}, ${data.country}: Currently ${data.current.temperature}°C, ${data.current.condition}. ${days}-day forecast.`
        )}],
        structuredContent: data,
      };
    }
  );

  server.registerTool(
    "refresh_weather",
    {
      description: "Refresh the weather forecast for a different city or date range.",
      inputSchema: {
        city: z.string().describe("City name"),
        days: z.number().min(1).max(14).optional(),
      },
      annotations: { readOnlyHint: true },
      _meta: { ui: { resourceUri: RESOURCE_URI, visibility: ["app"] } },
    },
    async (args) => {
      const data = generateForecast(args.city, args.days || 7);
      return {
        content: [{ type: "text" as const, text: disclaimerText(`Refreshed: ${data.city}, ${data.country} — ${data.current.temperature}°C.`) }],
        structuredContent: data,
      };
    }
  );
}
