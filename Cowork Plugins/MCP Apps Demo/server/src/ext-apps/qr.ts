/**
 * QR Code Generator — port of ext-apps qr-server.
 * Generates QR codes from text/URLs. Reimplemented in Node.js using qrcode npm.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import QRCode from "qrcode";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerQr(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/qr.html";

  server.resource("QR Code UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(qrWidgetHtml()),
    }],
  }));

  server.registerTool(
    "generate_qr",
    {
      description: "Generate a QR code from text or a URL. Returns an interactive QR code widget.",
      inputSchema: {
        text: z.string().describe("Text or URL to encode in the QR code"),
        size: z.number().min(100).max(1000).optional().describe("QR code size in pixels (default 300)"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const size = args.size || 300;
      const dataUrl = await QRCode.toDataURL(args.text, {
        width: size,
        margin: 2,
        color: { dark: "#000000", light: "#ffffff" },
      });

      return {
        content: [{ type: "text" as const, text: `QR code generated for: "${args.text.slice(0, 100)}"` }],
        structuredContent: {
          text: args.text,
          size,
          dataUrl,
        },
      };
    }
  );
}

function qrWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>QR Code</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:20px; display:flex;
    flex-direction:column; align-items:center; justify-content:center; min-height:100vh; }
  .qr-container { background:#fff; border-radius:12px; padding:24px; box-shadow:0 2px 8px rgba(0,0,0,0.08);
    text-align:center; }
  img { max-width:100%; border-radius:4px; }
  .text { font-size:12px; color:#666; margin-top:12px; word-break:break-all; max-width:300px; }
</style>
</head>
<body>
<div class="qr-container">
  <img id="qr" alt="QR Code">
  <div class="text" id="text"></div>
</div>
<script>
window.addEventListener("message", (e) => {
  if (e.data && e.data.structuredContent) {
    const d = e.data.structuredContent;
    document.getElementById("qr").src = d.dataUrl || "";
    document.getElementById("text").textContent = d.text || "";
  }
});
</script>
</body>
</html>`;
}
