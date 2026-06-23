/**
 * PDF Viewer — port of ext-apps pdf-server.
 * Renders PDFs using pdf.js from CDN.
 * NOTE: pdf.js is ~400KB. Loaded from CDN — may not render in strict CSP.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

const SAMPLE_PDFS: Record<string, { url: string; title: string }> = {
  arxiv: {
    url: "https://arxiv.org/pdf/1706.03762",
    title: "Attention Is All You Need (Vaswani et al., 2017)",
  },
  rfc: {
    url: "https://www.rfc-editor.org/rfc/rfc2616.txt",
    title: "RFC 2616 — HTTP/1.1",
  },
};

export function registerPdf(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/pdf.html";

  server.resource("PDF Viewer UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(pdfWidgetHtml()),
    }],
  }));

  server.registerTool(
    "show_pdf",
    {
      description: "Display a PDF document in an interactive viewer. Supports direct URLs or sample PDFs (arxiv, rfc).",
      inputSchema: {
        source: z.string().optional().describe("PDF URL or sample name (arxiv, rfc). Default: arxiv"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      const source = args.source || "arxiv";
      const sample = SAMPLE_PDFS[source.toLowerCase()];
      const pdfUrl = sample?.url || source;
      const title = sample?.title || "PDF Document";

      return {
        content: [{ type: "text" as const, text: `PDF Viewer: "${title}". Note: requires network access from the widget iframe.` }],
        structuredContent: { url: pdfUrl, title },
      };
    }
  );
}

function pdfWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>PDF Viewer</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#525659; }
  .toolbar { background:#323639; padding:8px 12px; display:flex; align-items:center; gap:12px; }
  .toolbar h1 { font-size:14px; color:#fff; font-weight:500; flex:1; overflow:hidden;
    text-overflow:ellipsis; white-space:nowrap; }
  .toolbar .page-info { font-size:12px; color:#aaa; }
  .toolbar button { background:#4361ee; border:none; color:#fff; padding:4px 10px; border-radius:4px;
    font-size:12px; cursor:pointer; }
  .toolbar button:hover { background:#3451de; }
  #viewer { overflow:auto; height:calc(100vh - 44px); display:flex; flex-direction:column;
    align-items:center; padding:16px; gap:8px; }
  canvas { box-shadow:0 2px 8px rgba(0,0,0,0.3); }
  .loading { color:#aaa; font-size:14px; padding:40px; text-align:center; }
  .error { color:#f85149; font-size:13px; padding:20px; text-align:center; }
</style>
<script src="https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.4.168/pdf.min.mjs" type="module"><\/script>
</head>
<body>
<div class="toolbar">
  <h1 id="title">PDF Viewer</h1>
  <span class="page-info" id="pageInfo"></span>
</div>
<div id="viewer"><div class="loading">Loading PDF...</div></div>

<script type="module">
try {
  const pdfjsLib = await import("https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.4.168/pdf.min.mjs");
  pdfjsLib.GlobalWorkerOptions.workerSrc = "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/4.4.168/pdf.worker.min.mjs";

  async function render(data) {
    document.getElementById("title").textContent = data.title || "PDF";
    const viewer = document.getElementById("viewer");
    viewer.innerHTML = '<div class="loading">Loading PDF...</div>';

    try {
      const pdf = await pdfjsLib.getDocument(data.url).promise;
      viewer.innerHTML = "";
      document.getElementById("pageInfo").textContent = pdf.numPages + " pages";

      for (let i = 1; i <= Math.min(pdf.numPages, 10); i++) {
        const page = await pdf.getPage(i);
        const scale = Math.min(600 / page.getViewport({ scale: 1 }).width, 2);
        const viewport = page.getViewport({ scale });
        const canvas = document.createElement("canvas");
        canvas.width = viewport.width;
        canvas.height = viewport.height;
        viewer.appendChild(canvas);
        await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
      }
      if (pdf.numPages > 10) {
        const note = document.createElement("div");
        note.className = "loading";
        note.textContent = "Showing first 10 of " + pdf.numPages + " pages.";
        viewer.appendChild(note);
      }
    } catch (err) {
      viewer.innerHTML = '<div class="error">Failed to load PDF. The source may be blocked by CSP or CORS.<br>' +
        err.message + '</div>';
    }
  }

  window.addEventListener("message", (e) => {
    if (e.data && e.data.structuredContent) render(e.data.structuredContent);
  });
} catch (err) {
  document.getElementById("viewer").innerHTML =
    '<div class="error">PDF.js could not load. CDN access may be blocked.</div>';
}
<\/script>
</body>
</html>`;
}
