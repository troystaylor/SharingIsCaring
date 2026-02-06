/**
 * Chat App - MCP Apps UI for agent responses
 * Renders inside an iframe in MCP-compatible hosts (VS Code, Claude Desktop, etc.)
 */

import { App } from "@modelcontextprotocol/ext-apps";

const appEl = document.getElementById("app")!;

// Simple markdown-to-HTML converter (no external deps)
function renderMarkdown(text: string): string {
  let html = text
    // Escape HTML entities first
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");

  // Code blocks
  html = html.replace(/```(\w*)\n([\s\S]*?)```/g, (_m, _lang, code) => {
    return `<pre><code>${code.trim()}</code></pre>`;
  });

  // Inline code
  html = html.replace(/`([^`]+)`/g, "<code>$1</code>");

  // Headers
  html = html.replace(/^### (.+)$/gm, "<h3>$1</h3>");
  html = html.replace(/^## (.+)$/gm, "<h2>$1</h2>");
  html = html.replace(/^# (.+)$/gm, "<h1>$1</h1>");

  // Bold
  html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");

  // Italic
  html = html.replace(/\*(.+?)\*/g, "<em>$1</em>");

  // Links
  html = html.replace(
    /\[([^\]]+)\]\(([^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener">$1</a>'
  );

  // Horizontal rules
  html = html.replace(/^---$/gm, "<hr>");

  // Tables
  html = html.replace(
    /^(\|.+\|)\n(\|[-| :]+\|)\n((?:\|.+\|\n?)+)/gm,
    (_match, headerRow: string, _sep: string, bodyRows: string) => {
      const headers = headerRow
        .split("|")
        .filter((c: string) => c.trim())
        .map((c: string) => `<th>${c.trim()}</th>`)
        .join("");
      const rows = bodyRows
        .trim()
        .split("\n")
        .map((row: string) => {
          const cells = row
            .split("|")
            .filter((c: string) => c.trim())
            .map((c: string) => `<td>${c.trim()}</td>`)
            .join("");
          return `<tr>${cells}</tr>`;
        })
        .join("");
      return `<table><thead><tr>${headers}</tr></thead><tbody>${rows}</tbody></table>`;
    }
  );

  // Unordered lists
  html = html.replace(/^- (.+)$/gm, "<li>$1</li>");
  html = html.replace(/((?:<li>.*<\/li>\n?)+)/g, "<ul>$1</ul>");

  // Paragraphs (double newline)
  html = html.replace(/\n\n+/g, "</p><p>");
  html = `<p>${html}</p>`;

  // Single newlines to <br> within paragraphs
  html = html.replace(/([^>])\n([^<])/g, "$1<br>$2");

  // Clean up empty paragraphs
  html = html.replace(/<p>\s*<\/p>/g, "");

  return html;
}

interface AgentToolResult {
  agentName?: string;
  conversationId?: string;
  response?: string;
  hasCards?: boolean;
  suggestedActions?: string[];
}

function renderResponse(data: AgentToolResult): void {
  let html = "";

  // Agent header
  if (data.agentName) {
    html += `<div class="agent-header">`;
    html += `<span class="agent-name">${data.agentName}</span>`;
    if (data.conversationId) {
      html += `<span class="conversation-id">${data.conversationId}</span>`;
    }
    html += `</div>`;
  }

  // Response body
  if (data.response) {
    html += `<div class="response-body">${renderMarkdown(data.response)}</div>`;
  }

  // Suggested actions
  if (data.suggestedActions && data.suggestedActions.length > 0) {
    html += `<div class="suggested-actions">`;
    for (const action of data.suggestedActions) {
      html += `<button class="action-btn" data-action="${action}">${action}</button>`;
    }
    html += `</div>`;
  }

  appEl.innerHTML = html;

  // Wire up suggested action buttons
  appEl.querySelectorAll(".action-btn").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const action = (btn as HTMLElement).dataset.action;
      if (!action) return;

      // Disable buttons while processing
      appEl
        .querySelectorAll(".action-btn")
        .forEach((b) => ((b as HTMLButtonElement).disabled = true));

      try {
        const result = await app.callServerTool({
          name: "chat_with_agent",
          arguments: { message: action },
        });
        const textItem = result.content?.find(
          (c) => c.type === "text"
        );
        const text = textItem && "text" in textItem ? textItem.text : undefined;
        if (text) {
          try {
            const parsed = JSON.parse(text);
            renderResponse(parsed);
          } catch {
            renderResponse({ response: text });
          }
        }
      } catch (err) {
        appEl.innerHTML += `<div class="error">Error: ${err}</div>`;
      }
    });
  });
}

// Create MCP App instance
const app = new App({ name: "Power Agent Chat", version: "1.0.0" });

// Handle incoming tool results
app.ontoolresult = (result) => {
  const textItem = result.content?.find(
    (c) => c.type === "text"
  );
  const text = textItem && "text" in textItem ? textItem.text : undefined;
  if (!text) {
    appEl.innerHTML = `<div class="error">No response received</div>`;
    return;
  }

  try {
    const data = JSON.parse(text) as AgentToolResult;
    renderResponse(data);
  } catch {
    // Plain text response
    renderResponse({ response: text });
  }
};

// Connect to host
app.connect();
