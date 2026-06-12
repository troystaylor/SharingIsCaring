/**
 * MCP Apps Demo — Server factory.
 * Creates an McpServer and registers all tools + resources from
 * custom examples and ext-apps ports.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

// Custom business examples
import { registerKanban } from "./custom/kanban.js";
import { registerSalesDashboard } from "./custom/sales-dashboard.js";
import { registerItDashboard } from "./custom/it-dashboard.js";
import { registerWeather } from "./custom/weather.js";
import { registerUnitConverter } from "./custom/unit-converter.js";

// Ext-apps ports
import { registerQr } from "./ext-apps/qr.js";
import { registerBasic } from "./ext-apps/basic.js";
import { registerBudgetAllocator } from "./ext-apps/budget-allocator.js";
import { registerCustomerSegment } from "./ext-apps/customer-segment.js";
import { registerCohortHeatmap } from "./ext-apps/cohort-heatmap.js";
import { registerScenarioModeler } from "./ext-apps/scenario-modeler.js";
import { registerMap } from "./ext-apps/map.js";
import { registerWikiExplorer } from "./ext-apps/wiki-explorer.js";
import { registerThree } from "./ext-apps/three.js";
import { registerShadertoy } from "./ext-apps/shadertoy.js";
import { registerSheetMusic } from "./ext-apps/sheet-music.js";
import { registerSystemMonitor } from "./ext-apps/system-monitor.js";
import { registerTranscript } from "./ext-apps/transcript.js";
import { registerVideo } from "./ext-apps/video.js";
import { registerPdf } from "./ext-apps/pdf.js";
import { registerSnake } from "./ext-apps/snake.js";
import { register2048 } from "./ext-apps/game-2048.js";
import { registerMinesweeper } from "./ext-apps/minesweeper.js";
import { registerTetris } from "./ext-apps/tetris.js";

export function createServer(): McpServer {
  const server = new McpServer({
    name: "MCP Apps Demo",
    version: "1.0.0",
  });

  // --- Custom business examples (with elicitation) ---
  registerKanban(server);
  registerSalesDashboard(server);
  registerItDashboard(server);
  registerWeather(server);
  registerUnitConverter(server);

  // --- Ext-apps ports (faithful to upstream, no elicitation) ---
  registerQr(server);
  registerBasic(server);
  registerBudgetAllocator(server);
  registerCustomerSegment(server);
  registerCohortHeatmap(server);
  registerScenarioModeler(server);
  registerMap(server);
  registerWikiExplorer(server);
  registerThree(server);
  registerShadertoy(server);
  registerSheetMusic(server);
  registerSystemMonitor(server);
  registerTranscript(server);
  registerVideo(server);
  registerPdf(server);

  // --- Classic games ---
  registerSnake(server);
  register2048(server);
  registerMinesweeper(server);
  registerTetris(server);

  return server;
}
