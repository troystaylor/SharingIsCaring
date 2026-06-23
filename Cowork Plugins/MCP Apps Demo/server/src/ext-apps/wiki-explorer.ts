/**
 * Wiki Explorer — port of ext-apps wiki-explorer-server.
 * Fetches Wikipedia article summaries and related links. Widget renders a force-directed graph.
 * Uses Wikipedia API (free, no key).
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

interface WikiNode { title: string; extract: string; links: string[]; }

async function fetchWikiArticle(title: string): Promise<WikiNode | null> {
  const url = `https://en.wikipedia.org/api/rest_v1/page/summary/${encodeURIComponent(title)}`;
  const res = await fetch(url, { headers: { "User-Agent": "MCP-Apps-Demo/1.0" } });
  if (!res.ok) return null;
  const data = await res.json() as { title: string; extract: string };

  // Fetch links
  const linksUrl = `https://en.wikipedia.org/w/api.php?action=query&titles=${encodeURIComponent(title)}&prop=links&pllimit=10&format=json&origin=*`;
  const linksRes = await fetch(linksUrl, { headers: { "User-Agent": "MCP-Apps-Demo/1.0" } });
  let links: string[] = [];
  if (linksRes.ok) {
    const linksData = await linksRes.json() as { query: { pages: Record<string, { links?: Array<{ title: string }> }> } };
    const pages = Object.values(linksData.query.pages);
    links = pages[0]?.links?.map((l) => l.title).filter((t) => !t.includes(":")) || [];
  }

  return { title: data.title, extract: data.extract, links: links.slice(0, 8) };
}

export function registerWikiExplorer(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/wiki.html";

  server.resource("Wiki Explorer UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(wikiWidgetHtml()),
    }],
  }));

  server.registerTool(
    "explore_wiki",
    {
      description: "Explore Wikipedia articles as a network graph. Enter a topic to see related articles and their connections.",
      inputSchema: {
        topic: z.string().describe("Wikipedia article title to start exploring from"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const article = await fetchWikiArticle(args.topic);
      if (!article) {
        return {
          content: [{ type: "text" as const, text: `Wikipedia article "${args.topic}" not found.` }],
          isError: true,
        };
      }

      return {
        content: [{ type: "text" as const, text: `${article.title}: ${article.extract.slice(0, 200)}... (${article.links.length} related articles)` }],
        structuredContent: {
          root: article.title,
          nodes: [{ id: article.title, extract: article.extract, isRoot: true }],
          edges: article.links.map((l) => ({ source: article.title, target: l })),
          relatedTitles: article.links,
        },
      };
    }
  );
}

function wikiWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Wiki Explorer</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#1a1a2e; color:#fff; }
  canvas { width:100%; height:100vh; display:block; }
  .info { position:absolute; top:10px; left:10px; background:rgba(0,0,0,0.7); padding:10px 14px;
    border-radius:8px; font-size:12px; max-width:300px; z-index:10; }
  .info h2 { font-size:14px; margin-bottom:4px; color:#7c3aed; }
  .info p { font-size:11px; color:#ccc; line-height:1.4; }
  .links { margin-top:8px; display:flex; flex-wrap:wrap; gap:4px; }
  .link-tag { background:#333; padding:2px 8px; border-radius:4px; font-size:10px; color:#aaa; }
</style>
</head>
<body>
<div class="info" id="info"><h2>Wiki Explorer</h2><p>Loading...</p></div>
<canvas id="graph"></canvas>
<script>
(function() {
  function render(data) {
    const info = document.getElementById("info");
    const root = data.nodes.find(n => n.isRoot);
    info.innerHTML = '<h2>' + esc(data.root) + '</h2>' +
      '<p>' + esc((root?.extract || '').slice(0, 200)) + '...</p>' +
      '<div class="links">' + data.relatedTitles.map(t =>
        '<span class="link-tag">' + esc(t) + '</span>'
      ).join('') + '</div>';

    // Simple force-directed layout on canvas
    const canvas = document.getElementById("graph");
    const ctx = canvas.getContext("2d");
    const w = canvas.width = canvas.offsetWidth;
    const h = canvas.height = canvas.offsetHeight;
    const cx = w/2, cy = h/2;

    const allNodes = [{ id: data.root, x: cx, y: cy, r: 20 }];
    data.relatedTitles.forEach((t, i) => {
      const angle = (i / data.relatedTitles.length) * Math.PI * 2;
      const dist = 120 + Math.random() * 40;
      allNodes.push({ id: t, x: cx + Math.cos(angle) * dist, y: cy + Math.sin(angle) * dist, r: 10 });
    });

    ctx.clearRect(0, 0, w, h);

    // Edges
    ctx.strokeStyle = "rgba(124,58,237,0.3)"; ctx.lineWidth = 1;
    data.edges.forEach(e => {
      const s = allNodes.find(n => n.id === e.source);
      const t = allNodes.find(n => n.id === e.target);
      if (s && t) { ctx.beginPath(); ctx.moveTo(s.x, s.y); ctx.lineTo(t.x, t.y); ctx.stroke(); }
    });

    // Nodes
    allNodes.forEach(n => {
      ctx.fillStyle = n.r > 15 ? "#7c3aed" : "#4361ee";
      ctx.beginPath(); ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = "#fff"; ctx.font = (n.r > 15 ? "11" : "9") + "px system-ui";
      ctx.textAlign = "center"; ctx.textBaseline = "middle";
      const label = n.id.length > 15 ? n.id.slice(0, 14) + "..." : n.id;
      ctx.fillText(label, n.x, n.y + n.r + 12);
    });
  }

  function esc(s) { const d = document.createElement("div"); d.textContent = s||""; return d.innerHTML; }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
})();
<\/script>
</body>
</html>`;
}
