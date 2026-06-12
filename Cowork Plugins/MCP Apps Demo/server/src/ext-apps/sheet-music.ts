/**
 * Sheet Music — port of ext-apps sheet-music-server.
 * Renders ABC notation as sheet music. Widget uses inline SVG rendering
 * (simplified — upstream uses abcjs which is ~200KB).
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

const EXAMPLE_ABC = `X:1
T:Twinkle Twinkle Little Star
M:4/4
L:1/4
K:C
C C G G | A A G2 | F F E E | D D C2 |
G G F F | E E D2 | G G F F | E E D2 |
C C G G | A A G2 | F F E E | D D C2 |`;

export function registerSheetMusic(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/sheet-music.html";

  server.resource("Sheet Music UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(sheetMusicWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_sheet_music",
    {
      description: "Display sheet music from ABC notation. Renders as visual notation with playback controls.",
      inputSchema: {
        abc: z.string().optional().describe("ABC notation string. If omitted, shows 'Twinkle Twinkle Little Star'."),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const abc = args.abc || EXAMPLE_ABC;
      const title = abc.match(/T:(.+)/)?.[1]?.trim() || "Untitled";
      return {
        content: [{ type: "text" as const, text: `Sheet Music: "${title}" — ${abc.length} characters of ABC notation.` }],
        structuredContent: { abc, title },
      };
    }
  );
}

function sheetMusicWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Sheet Music</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#faf9f6; padding:20px; }
  h1 { font-size:18px; font-weight:600; color:#333; margin-bottom:4px; }
  .abc-source { background:#fff; border:1px solid #e0e0e0; border-radius:8px; padding:16px;
    font-family:"Courier New",monospace; font-size:13px; white-space:pre-wrap; color:#555;
    line-height:1.6; margin-top:12px; }
  .note { font-size:12px; color:#888; margin-top:12px; font-style:italic; }
</style>
<!-- abcjs from CDN for rendering -->
<script src="https://cdn.jsdelivr.net/npm/abcjs@6.4.4/dist/abcjs-basic-min.js"><\/script>
</head>
<body>
<h1 id="title">Sheet Music</h1>
<div id="notation"></div>
<details style="margin-top:16px">
  <summary style="font-size:12px;color:#888;cursor:pointer">ABC Source</summary>
  <div class="abc-source" id="source"></div>
</details>
<div class="note">Rendered with abcjs. In strict CSP environments, only the source notation is shown.</div>

<script>
(function() {
  function render(data) {
    document.getElementById("title").textContent = data.title || "Sheet Music";
    document.getElementById("source").textContent = data.abc || "";
    try {
      if (window.ABCJS) {
        ABCJS.renderAbc("notation", data.abc, {
          responsive: "resize",
          add_classes: true
        });
      }
    } catch (err) {
      console.error("abcjs render failed:", err);
    }
  }
  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
