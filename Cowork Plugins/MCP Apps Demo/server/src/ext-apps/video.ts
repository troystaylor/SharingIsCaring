/**
 * Video Resource — port of ext-apps video-resource-server.
 * Demonstrates serving video content via MCP resources.
 * Widget embeds a video player with standard controls.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

// Sample videos from public domain / CC0 sources
const SAMPLE_VIDEOS: Record<string, { url: string; title: string; duration: string }> = {
  nature: {
    url: "https://storage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4",
    title: "For Bigger Blazes",
    duration: "0:15",
  },
  tears: {
    url: "https://storage.googleapis.com/gtv-videos-bucket/sample/TearsOfSteel.mp4",
    title: "Tears of Steel (Open Movie)",
    duration: "12:14",
  },
  sintel: {
    url: "https://storage.googleapis.com/gtv-videos-bucket/sample/Sintel.mp4",
    title: "Sintel (Open Movie)",
    duration: "14:48",
  },
};

export function registerVideo(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/video.html";

  server.resource("Video Player UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(videoWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_video",
    {
      description: "Display a video player widget. Supports direct URLs or sample videos (nature, tears, sintel).",
      inputSchema: {
        source: z.string().optional().describe("Video URL or sample name (nature, tears, sintel). Default: nature"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const source = args.source || "nature";
      const sample = SAMPLE_VIDEOS[source.toLowerCase()];

      const videoUrl = sample?.url || source;
      const title = sample?.title || "Custom Video";
      const duration = sample?.duration || "Unknown";

      return {
        content: [{ type: "text" as const, text: `Video: "${title}" (${duration}). Note: video requires network access from the widget iframe.` }],
        structuredContent: { url: videoUrl, title, duration },
      };
    }
  );
}

function videoWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Video Player</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#000; display:flex; flex-direction:column;
    align-items:center; justify-content:center; min-height:100vh; padding:16px; }
  .container { max-width:640px; width:100%; }
  h1 { font-size:16px; color:#fff; margin-bottom:8px; }
  .meta { font-size:12px; color:#888; margin-bottom:12px; }
  video { width:100%; border-radius:8px; background:#111; }
  .note { font-size:11px; color:#666; margin-top:8px; text-align:center; }
</style>
</head>
<body>
<div class="container">
  <h1 id="title">Video Player</h1>
  <div class="meta" id="meta"></div>
  <video id="player" controls preload="metadata">
    Your browser does not support video playback.
  </video>
  <div class="note">Video streaming requires network access from the widget iframe. May not work in all CSP configurations.</div>
</div>
<script>
window.addEventListener("message", (e) => {
  if (e.data && e.data.structuredContent) {
    const d = e.data.structuredContent;
    document.getElementById("title").textContent = d.title || "Video";
    document.getElementById("meta").textContent = "Duration: " + (d.duration || "Unknown");
    const player = document.getElementById("player");
    player.src = d.url || "";
  }
});
<\/script>
</body>
</html>`;
}
