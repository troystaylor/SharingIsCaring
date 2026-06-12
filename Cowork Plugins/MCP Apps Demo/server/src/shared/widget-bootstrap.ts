/**
 * MCP Apps widget bootstrap — implements the minimal MCP Apps postMessage
 * handshake inline (no external SDK import, works under Cowork's strict CSP).
 *
 * Provides:
 * - JSON-RPC 2.0 handshake (ui/initialize → ui/notifications/initialized)
 * - Tool result bridging to window MessageEvent for existing render handlers
 * - ResizeObserver-based auto-sizing (reports content height to host continuously)
 * - window.__mcpCallTool(name, args) for widget→server tool calls (e.g., move_card)
 */

const BOOTSTRAP_SCRIPT = `
<script>
(function() {
  var msgId = 1;
  var pending = {};

  window.addEventListener("message", function(event) {
    if (event.source !== window.parent) return;
    var msg = event.data;
    if (!msg || msg.jsonrpc !== "2.0") return;

    // Response to our request
    if (msg.id !== undefined && msg.id !== null && pending[msg.id]) {
      pending[msg.id](msg.result, msg.error);
      delete pending[msg.id];
      return;
    }

    // Notification from host
    if (msg.method) {
      if (msg.method === "ui/notifications/tool-result" ||
          msg.method === "ui/notifications/tool-input") {
        var p = msg.params || {};
        window.dispatchEvent(new MessageEvent("message", {
          data: { structuredContent: p.structuredContent || p.arguments || p }
        }));
        return;
      }
      if (msg.method === "ui/notifications/host-context-changed") return;
      if (msg.method === "ui/notifications/tool-cancelled") return;
    }

    // Request from host (ping, teardown)
    if (msg.id !== undefined && msg.method) {
      window.parent.postMessage({ jsonrpc: "2.0", id: msg.id, result: {} }, "*");
      return;
    }
  });

  function sendRequest(method, params) {
    return new Promise(function(resolve, reject) {
      var id = msgId++;
      pending[id] = function(result, error) {
        if (error) reject(error); else resolve(result);
      };
      window.parent.postMessage({
        jsonrpc: "2.0", id: id, method: method, params: params
      }, "*");
    });
  }

  // Expose callServerTool globally for widgets (e.g., kanban move_card)
  window.__mcpCallTool = function(name, args) {
    return sendRequest("tools/call", { name: name, arguments: args || {} });
  };

  // Auto-resize: report content height to host.
  // Only report height — let Cowork control width to avoid feedback loops.
  // Use a 200ms debounce to prevent cascading resize → re-render → resize.
  var lastH = 0, resizeTimer = null;
  function reportSize() {
    if (resizeTimer) return;
    resizeTimer = setTimeout(function() {
      resizeTimer = null;
      // Measure height via max-content to get true content height
      var el = document.documentElement;
      var origH = el.style.height;
      el.style.height = "max-content";
      var h = Math.ceil(el.getBoundingClientRect().height);
      el.style.height = origH;
      // Only report if height actually changed (ignore width — host controls it)
      if (h !== lastH && h > 0) {
        lastH = h;
        window.parent.postMessage({
          jsonrpc: "2.0",
          method: "ui/notifications/size-changed",
          params: { width: Math.ceil(window.innerWidth), height: h }
        }, "*");
      }
    }, 200);
  }

  // Handshake: ui/initialize -> ui/notifications/initialized
  sendRequest("ui/initialize", {
    appCapabilities: { tools: { listChanged: true } },
    appInfo: { name: "MCP Apps Demo Widget", version: "1.0.0" },
    protocolVersion: "2026-01-26"
  }).then(function() {
    window.parent.postMessage({
      jsonrpc: "2.0",
      method: "ui/notifications/initialized"
    }, "*");
    // Report size once after content loads, then observe for changes
    setTimeout(reportSize, 300);
    if (typeof ResizeObserver !== "undefined") {
      var ro = new ResizeObserver(reportSize);
      ro.observe(document.body);
    }
    // No MutationObserver — too noisy, causes feedback loops
  }).catch(function(e) {
    console.warn("[MCP Apps bootstrap] Handshake failed:", e);
  });
})();
<\/script>`;

/**
 * Injects the MCP Apps bootstrap script into widget HTML.
 * Places it before </body> so it loads after the widget's own scripts.
 */
export function injectBootstrap(html: string): string {
  if (html.includes("</body>")) {
    return html.replace("</body>", `${BOOTSTRAP_SCRIPT}\n</body>`);
  }
  return html + BOOTSTRAP_SCRIPT;
}
