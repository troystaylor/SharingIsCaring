/**
 * Basic Demo — port of ext-apps basic server (Vanilla JS template).
 * Minimal widget showing the MCP Apps handshake and data flow.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerBasic(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/basic.html";

  server.resource("Basic Demo UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(basicWidgetHtml()),
    }],
  }));

  server.registerTool(
    "basic_demo",
    {
      description: "Display a basic MCP Apps demo widget showing how tool data flows to a rendered UI.",
      inputSchema: {
        message: z.string().optional().describe("Message to display in the widget (default: 'Hello from MCP Apps!')"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const message = args.message || "Hello from MCP Apps!";
      return {
        content: [{ type: "text" as const, text: `Basic demo widget displayed with message: "${message}"` }],
        structuredContent: {
          message,
          timestamp: new Date().toISOString(),
          serverName: "MCP Apps Demo",
          features: [
            "Tool data → structuredContent → widget",
            "resources/read serves HTML with text/html;profile=mcp-app",
            "Graceful degradation (text content works without widget)",
          ],
        },
      };
    }
  );
}

function basicWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Basic MCP Apps Demo</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f0f2f5; padding:20px; }
  .card { background:#fff; border-radius:12px; padding:24px; max-width:480px; margin:0 auto;
    box-shadow:0 2px 8px rgba(0,0,0,0.08); }
  h1 { font-size:18px; color:#1a1a2e; margin-bottom:8px; }
  .message { font-size:24px; font-weight:700; color:#4361ee; margin:16px 0; text-align:center;
    padding:20px; background:#f0f4ff; border-radius:8px; }
  .meta { font-size:12px; color:#888; margin-bottom:12px; }
  .features { list-style:none; }
  .features li { font-size:13px; color:#555; padding:6px 0; border-bottom:1px solid #f0f0f0; }
  .features li::before { content:"\\2713 "; color:#4361ee; font-weight:bold; }
</style>
</head>
<body>
<div class="card">
  <h1>MCP Apps Demo</h1>
  <div class="meta" id="meta"></div>
  <div class="message" id="message">Waiting for data...</div>
  <h2 style="font-size:14px;color:#555;margin:12px 0 8px;">How it works</h2>
  <ul class="features" id="features"></ul>
</div>
<script>
window.addEventListener("message", (e) => {
  if (e.data && e.data.structuredContent) {
    const d = e.data.structuredContent;
    document.getElementById("message").textContent = d.message || "";
    document.getElementById("meta").textContent = "Server: " + (d.serverName || "") + " | " + (d.timestamp || "");
    const ul = document.getElementById("features");
    ul.innerHTML = (d.features || []).map(f => "<li>" + f + "</li>").join("");
  }
});
</script>
</body>
</html>`;
}
