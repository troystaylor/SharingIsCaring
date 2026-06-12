/**
 * Demo data disclaimer — injected into widget HTML footers and tool response text
 * for any tool that returns mock/synthetic data.
 */

import { injectBootstrap } from "./widget-bootstrap.js";

export const DEMO_DISCLAIMER =
  "This tool displays demo data for illustration purposes. No real data is shown.";

/**
 * Injects a disclaimer banner AND the MCP Apps bootstrap into widget HTML.
 * Finds the closing </body> tag and inserts both before it.
 */
export function injectDisclaimer(html: string): string {
  const banner = `
<div style="position:fixed;bottom:0;left:0;right:0;background:#fff3cd;color:#856404;
  border-top:1px solid #ffc107;padding:6px 12px;font-size:12px;text-align:center;
  font-family:system-ui,sans-serif;z-index:9999;">
  &#9888; ${DEMO_DISCLAIMER}
</div>`;

  let result = html;
  if (result.includes("</body>")) {
    result = result.replace("</body>", `${banner}\n</body>`);
  } else {
    result = result + banner;
  }
  return injectBootstrap(result);
}

/**
 * Returns a text content block prefixed with the disclaimer.
 * Use as the first element in a tool's content[] array for mock-data tools.
 */
export function disclaimerText(summary: string): string {
  return `[Demo Data] ${DEMO_DISCLAIMER}\n\n${summary}`;
}
