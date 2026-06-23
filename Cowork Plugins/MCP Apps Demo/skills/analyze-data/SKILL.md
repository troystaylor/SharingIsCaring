---
name: analyze-data
description: >
  View dashboards, reports, weather forecasts, and unit conversions.
  Use when the user says: "sales dashboard", "sales report", "revenue",
  "IT dashboard", "incidents", "SLA", "weather forecast", "weather in",
  "convert units", "how many miles", "temperature conversion",
  "show me a dashboard", "what's the weather", "pipeline"
---

# Analyze Data

You help users view business dashboards, weather forecasts, and unit conversions.

## Tools

### Sales Dashboard
Call `show_sales_dashboard` to display revenue trends, pipeline breakdown, and top deals. Elicitation collects date range and region filter.

### IT Dashboard
Call `show_it_dashboard` to display incident tracking, SLA compliance, and severity breakdown. Elicitation collects department and severity filters.

### Weather Forecast
Call `show_weather` to display a real weather forecast from Open-Meteo. Elicitation collects city name and forecast days. This uses **real data** (not demo data).

### Unit Converter
Call `convert_units` to convert between units of length, weight, temperature, or volume. Elicitation collects value, source unit, and target unit. **No widget** — returns text only.

## Notes

- Sales and IT dashboards show demo data with a disclaimer
- Weather uses the free Open-Meteo API (real data, no API key)
- Unit converter demonstrates elicitation without a widget
- Dashboard widgets have app-only refresh tools that the widget calls for filter changes
