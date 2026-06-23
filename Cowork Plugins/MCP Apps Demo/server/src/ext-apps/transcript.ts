/**
 * Transcript — port of ext-apps transcript-server.
 * Speech-to-text using the browser's Web Speech API.
 * No external dependencies. Widget uses built-in SpeechRecognition.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { injectBootstrap } from "../shared/widget-bootstrap.js";

export function registerTranscript(server: McpServer): void {
  const RESOURCE_URI = "ui://demo/transcript.html";

  server.resource("Transcript UI", RESOURCE_URI, async () => ({
    contents: [{
      uri: RESOURCE_URI,
      mimeType: "text/html;profile=mcp-app" as const,
      text: injectBootstrap(transcriptWidgetHtml()),
    }],
  }));

  server.registerTool(
    "transcribe_audio",
    {
      description: "Open a live speech-to-text transcription widget using the browser's Web Speech API. Speak into your microphone to generate text.",
      inputSchema: {
        language: z.string().optional().describe("BCP 47 language code (default 'en-US')"),
      },
      annotations: { readOnlyHint: true },

      _meta: { ui: { resourceUri: RESOURCE_URI } },
    },
    async (args) => {
      return {
        content: [{ type: "text" as const, text: `Transcript widget opened. Language: ${args.language || "en-US"}. Click "Start" and speak.` }],
        structuredContent: { language: args.language || "en-US" },
      };
    }
  );
}

function transcriptWidgetHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Transcript</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  body { font-family:system-ui,sans-serif; background:#f8f9fa; padding:20px; }
  h1 { font-size:18px; font-weight:600; color:#1a1a2e; margin-bottom:12px; }
  .controls { display:flex; gap:8px; margin-bottom:16px; }
  button { padding:8px 16px; border:none; border-radius:6px; font-size:13px; font-weight:600;
    cursor:pointer; transition:background 0.2s; }
  .btn-start { background:#2e7d32; color:#fff; }
  .btn-start:hover { background:#1b5e20; }
  .btn-stop { background:#c62828; color:#fff; }
  .btn-stop:hover { background:#b71c1c; }
  .btn-stop:disabled, .btn-start:disabled { opacity:0.5; cursor:default; }
  .status { font-size:12px; color:#888; margin-bottom:8px; }
  .status .dot { display:inline-block; width:8px; height:8px; border-radius:50%; margin-right:4px; }
  .dot.recording { background:#c62828; animation:pulse 1s infinite; }
  .dot.idle { background:#888; }
  @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
  .transcript { background:#fff; border:1px solid #e0e0e0; border-radius:8px; padding:16px;
    min-height:200px; font-size:14px; line-height:1.6; color:#333; white-space:pre-wrap; }
  .interim { color:#999; font-style:italic; }
  .error { color:#c62828; font-size:13px; margin-top:8px; }
</style>
</head>
<body>
<h1>Live Transcript</h1>
<div class="controls">
  <button class="btn-start" id="startBtn" onclick="startRec()">Start</button>
  <button class="btn-stop" id="stopBtn" onclick="stopRec()" disabled>Stop</button>
</div>
<div class="status"><span class="dot idle" id="dot"></span><span id="statusText">Ready</span></div>
<div class="transcript" id="transcript"></div>
<div class="error" id="error"></div>

<script>
let recognition = null;
let fullText = "";
let lang = "en-US";

window.addEventListener("message", (e) => {
  if (e.data && e.data.structuredContent) {
    lang = e.data.structuredContent.language || "en-US";
  }
});

function startRec() {
  const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SR) {
    document.getElementById("error").textContent = "Web Speech API not supported in this browser.";
    return;
  }
  recognition = new SR();
  recognition.continuous = true;
  recognition.interimResults = true;
  recognition.lang = lang;

  recognition.onstart = () => {
    document.getElementById("dot").className = "dot recording";
    document.getElementById("statusText").textContent = "Recording...";
    document.getElementById("startBtn").disabled = true;
    document.getElementById("stopBtn").disabled = false;
  };

  recognition.onresult = (e) => {
    let interim = "";
    let final = "";
    for (let i = e.resultIndex; i < e.results.length; i++) {
      if (e.results[i].isFinal) final += e.results[i][0].transcript + " ";
      else interim += e.results[i][0].transcript;
    }
    if (final) fullText += final;
    document.getElementById("transcript").innerHTML =
      esc(fullText) + (interim ? '<span class="interim">' + esc(interim) + '</span>' : '');
  };

  recognition.onerror = (e) => {
    document.getElementById("error").textContent = "Error: " + e.error;
    stopRec();
  };

  recognition.onend = () => stopRec();
  recognition.start();
}

function stopRec() {
  if (recognition) { try { recognition.stop(); } catch {} }
  document.getElementById("dot").className = "dot idle";
  document.getElementById("statusText").textContent = "Stopped";
  document.getElementById("startBtn").disabled = false;
  document.getElementById("stopBtn").disabled = true;
}

function esc(s) { const d = document.createElement("div"); d.textContent = s||""; return d.innerHTML; }
<\/script>
</body>
</html>`;
}
