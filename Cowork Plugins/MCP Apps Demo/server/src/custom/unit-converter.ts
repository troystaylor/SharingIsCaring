/**
 * Unit Converter — NO widget, text-only response.
 * Demonstrates elicitation without a widget, plus capability negotiation fallback.
 * Tool: convert_units (model only)
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { disclaimerText } from "../shared/disclaimer.js";
import { tryElicitate } from "../shared/elicitation.js";

const UNITS: Record<string, Record<string, { toBase: (v: number) => number; fromBase: (v: number) => number }>> = {
  length: {
    meters:      { toBase: (v) => v,           fromBase: (v) => v },
    kilometers:  { toBase: (v) => v * 1000,    fromBase: (v) => v / 1000 },
    miles:       { toBase: (v) => v * 1609.344, fromBase: (v) => v / 1609.344 },
    feet:        { toBase: (v) => v * 0.3048,  fromBase: (v) => v / 0.3048 },
    inches:      { toBase: (v) => v * 0.0254,  fromBase: (v) => v / 0.0254 },
    centimeters: { toBase: (v) => v * 0.01,    fromBase: (v) => v / 0.01 },
  },
  weight: {
    kilograms: { toBase: (v) => v,            fromBase: (v) => v },
    grams:     { toBase: (v) => v / 1000,     fromBase: (v) => v * 1000 },
    pounds:    { toBase: (v) => v * 0.453592,  fromBase: (v) => v / 0.453592 },
    ounces:    { toBase: (v) => v * 0.0283495, fromBase: (v) => v / 0.0283495 },
  },
  temperature: {
    celsius:    { toBase: (v) => v,                   fromBase: (v) => v },
    fahrenheit: { toBase: (v) => (v - 32) * 5 / 9,   fromBase: (v) => v * 9 / 5 + 32 },
    kelvin:     { toBase: (v) => v - 273.15,          fromBase: (v) => v + 273.15 },
  },
  volume: {
    liters:     { toBase: (v) => v,            fromBase: (v) => v },
    milliliters:{ toBase: (v) => v / 1000,     fromBase: (v) => v * 1000 },
    gallons:    { toBase: (v) => v * 3.78541,  fromBase: (v) => v / 3.78541 },
    cups:       { toBase: (v) => v * 0.236588, fromBase: (v) => v / 0.236588 },
    fluid_ounces:{ toBase: (v) => v * 0.0295735, fromBase: (v) => v / 0.0295735 },
  },
};

const ALL_UNITS = Object.values(UNITS).flatMap((cat) => Object.keys(cat));

function convert(value: number, from: string, to: string): { result: number; category: string } | null {
  for (const [category, units] of Object.entries(UNITS)) {
    if (units[from] && units[to]) {
      const base = units[from].toBase(value);
      const result = units[to].fromBase(base);
      return { result, category };
    }
  }
  return null;
}

export function registerUnitConverter(server: McpServer): void {
  // No _meta.ui — this tool has no widget
  server.registerTool(
    "convert_units",
    {
      description: "Convert a value between units (length, weight, temperature, volume). No widget — demonstrates elicitation-only pattern with text response.",
      inputSchema: {
        value: z.number().optional().describe("The numeric value to convert"),
        from: z.string().optional().describe("Source unit (e.g., 'miles', 'celsius', 'pounds')"),
        to: z.string().optional().describe("Target unit (e.g., 'kilometers', 'fahrenheit', 'kilograms')"),
      },
      annotations: { readOnlyHint: true },
    },
    async (args: { value?: number; from?: string; to?: string }) => {
      // Try elicitation — if the host supports it, collect all 3 fields via form
      const elicited = await tryElicitate<{ value: number; from: string; to: string }>(
        server, "Enter the conversion details.", {
          type: "object",
          properties: {
            value: { type: "number" as any, title: "Value", description: "Number to convert" },
            from: { type: "string", title: "From Unit", enum: ALL_UNITS, description: "Source unit" },
            to: { type: "string", title: "To Unit", enum: ALL_UNITS, description: "Target unit" },
          },
          required: ["value", "from", "to"],
        }
      );

      const value = elicited?.value ?? args.value;
      const from = elicited?.from || args.from;
      const to = elicited?.to || args.to;

      if (value === undefined || !from || !to) {
        return {
          content: [{ type: "text" as const, text: "Please provide a value, source unit, and target unit. Available units: " + ALL_UNITS.join(", ") }],
          isError: true,
        };
      }

      const result = convert(value, from, to);
      if (!result) {
        return {
          content: [{ type: "text" as const, text: `Cannot convert between "${from}" and "${to}". They must be in the same category (length, weight, temperature, or volume).` }],
          isError: true,
        };
      }

      const formatted = Number.isInteger(result.result) ? result.result.toString() : result.result.toFixed(4);
      return {
        content: [{ type: "text" as const, text: disclaimerText(
          `${value} ${from} = ${formatted} ${to} (${result.category})`
        )}],
      };
    }
  );
}
