/**
 * Power Agent Tray - Electron Main Process
 * System tray application exposing Copilot Studio agents via MCP protocol
 *
 * Modes:
 * - Default: Run as system tray app with UI for auth management
 * - --stdio: Run as MCP stdio server (for use with MCP clients)
 */

import { app, BrowserWindow, Tray, Menu, nativeImage, shell, dialog, clipboard } from "electron";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import * as path from "path";
import * as dotenv from "dotenv";

import { AuthService } from "./auth-service.js";
import { AgentClient } from "./agent-client.js";
import { createMcpServer } from "./mcp-server.js";
import { logger } from "./logger.js";

// Load environment variables
dotenv.config();

// ───────────────────────────────────────────────────
// Configuration from environment
// ───────────────────────────────────────────────────
const CLIENT_ID = process.env.CLIENT_ID || "";
const TENANT_ID = process.env.TENANT_ID || "";
const AGENT_NAME = process.env.AGENT_NAME || "";
const ENVIRONMENT_ID = process.env.ENVIRONMENT_ID || "";
const DIRECT_CONNECT_URL = process.env.DIRECT_CONNECT_URL || "";
const SCHEMA_NAME = process.env.SCHEMA_NAME || "";

/**
 * Extract a readable agent name from the Direct Connect URL or env vars
 */
function getAgentDisplayName(): string {
  if (AGENT_NAME) return AGENT_NAME;
  if (SCHEMA_NAME) return SCHEMA_NAME;

  // Extract from Direct Connect URL: .../bots/{schemaName}/conversations
  if (DIRECT_CONNECT_URL) {
    const match = DIRECT_CONNECT_URL.match(/\/bots\/([^/]+)\//i);
    if (match) {
      // Convert schema name like "tst_computerPartsStoreAssistant" to readable
      return match[1]
        .replace(/^\w+_/, "") // remove prefix like "tst_"
        .replace(/([A-Z])/g, " $1") // camelCase to spaces
        .replace(/^./, (s) => s.toUpperCase()) // capitalize first
        .trim();
    }
  }

  return "Not configured";
}

// ───────────────────────────────────────────────────
// Services
// ───────────────────────────────────────────────────
let authService: AuthService;
let agentClient: AgentClient;
let tray: Tray | null = null;

let authWindow: BrowserWindow | null = null;

/**
 * Validate required environment variables
 */
function validateConfig(): boolean {
  const missing: string[] = [];
  if (!CLIENT_ID) missing.push("CLIENT_ID");
  if (!TENANT_ID) missing.push("TENANT_ID");
  if (!ENVIRONMENT_ID && !DIRECT_CONNECT_URL) {
    missing.push("ENVIRONMENT_ID or DIRECT_CONNECT_URL");
  }

  if (missing.length > 0) {
    console.error(
      `Missing required environment variables: ${missing.join(", ")}`
    );
    console.error("Create a .env file with the required configuration.");
    return false;
  }
  return true;
}

/**
 * Initialize services
 */
async function initializeServices(): Promise<void> {
  authService = new AuthService({
    clientId: CLIENT_ID,
    tenantId: TENANT_ID,
  });

  await authService.initialize();

  agentClient = new AgentClient(authService, {
    directConnectUrl: DIRECT_CONNECT_URL || undefined,
    environmentId: ENVIRONMENT_ID || undefined,
    schemaName: SCHEMA_NAME || AGENT_NAME || undefined,
  });
}

/**
 * Handle login request - opens browser for PKCE auth
 */
async function handleLogin(): Promise<void> {
  try {
    const { url, port } = await authService.startLogin();
    const callbackPromise = authService.startCallbackServer(port);

    // Open Electron popup window for authentication
    authWindow = new BrowserWindow({
      width: 500,
      height: 700,
      title: "Sign in \u2014 Power Agent Tray",
      autoHideMenuBar: true,
      resizable: true,
      webPreferences: {
        nodeIntegration: false,
        contextIsolation: true,
      },
    });

    authWindow.loadURL(url);
    authWindow.show();

    // Clean up reference when window is closed
    authWindow.on("closed", () => {
      authWindow = null;
    });

    // Wait for callback
    await callbackPromise;
    console.log("[Main] Login successful");

    // Close auth window on success
    if (authWindow && !authWindow.isDestroyed()) {
      authWindow.close();
      authWindow = null;
    }

    // Update tray menu to reflect new state
    if (tray) {
      updateTrayMenu();
    }
  } catch (error) {
    console.error("[Main] Login failed:", error);
    throw error;
  }
}

/**
 * Create the system tray icon and menu
 */
function createTray(): void {
  // Load tray icon
  const iconPath = path.join(__dirname, "..", "assets", "electron.ico");
  const icon = nativeImage.createFromPath(iconPath);

  tray = new Tray(icon);
  tray.setToolTip("Power Agent Tray - MCP Server");

  updateTrayMenu();

  tray.on("click", () => {
    tray?.popUpContextMenu();
  });
}

/**
 * Update the tray context menu based on current state
 */
async function updateTrayMenu(): Promise<void> {
  if (!tray) return;

  const isAuthed = await authService.isAuthenticated();
  const user = isAuthed ? await authService.getCurrentUser() : null;

  const template: Electron.MenuItemConstructorOptions[] = [
    {
      label: getAgentDisplayName(),
      type: "normal",
      enabled: false,
    },
    { type: "separator" },
    {
      label: isAuthed
        ? `\u2713 ${user || "unknown"}`
        : "\u25CB Not signed in",
      type: "normal",
      enabled: false,
    },
    {
      label: isAuthed ? "Sign out" : "Sign in",
      type: "normal",
      click: async () => {
        if (isAuthed) {
          await authService.logout();
          await agentClient.endConversation();
          updateTrayMenu();
        } else {
          try {
            await handleLogin();
          } catch (error) {
            dialog.showErrorBox(
              "Authentication Failed",
              error instanceof Error ? error.message : String(error)
            );
          }
        }
      },
    },
    { type: "separator" },
    {
      label: "Start with Windows",
      type: "checkbox",
      checked: app.getLoginItemSettings().openAtLogin,
      click: (menuItem) => {
        app.setLoginItemSettings({ openAtLogin: menuItem.checked });
        console.log(
          `[Main] Auto-start ${menuItem.checked ? "enabled" : "disabled"}`
        );
      },
    },
    {
      label: "Open log folder",
      type: "normal",
      click: () => {
        shell.openPath(logger.getLogDir());
      },
    },
    {
      label: "Copy VS Code MCP config",
      type: "normal",
      click: () => {
        const electronPath = path.join(
          __dirname, "..", "node_modules", "electron", "dist", "electron.exe"
        );
        const cwd = path.join(__dirname, "..");
        const serverEntry = {
          type: "stdio",
          command: electronPath,
          args: ["dist/main.js", "--stdio"],
          cwd,
        };
        const config = `"power-agent-tray": ${JSON.stringify(serverEntry, null, 2)}`;
        clipboard.writeText(config);
        tray?.displayBalloon({
          title: "Copied!",
          content: "VS Code MCP server config copied to clipboard.",
          iconType: "info",
        });
      },
    },
    { type: "separator" },
    {
      label: "Quit",
      type: "normal",
      click: () => {
        logger.close();
        app.quit();
      },
    },
  ];

  const contextMenu = Menu.buildFromTemplate(template);
  tray.setContextMenu(contextMenu);
}

/**
 * Run in MCP stdio mode (for use with MCP clients)
 */
async function runStdioMode(): Promise<void> {
  // In stdio mode, stdout is reserved for MCP JSON messages.
  // Suppress console.log/warn to avoid VS Code showing them as warnings.
  const noop = () => {};
  console.log = noop;
  console.warn = noop;

  if (!validateConfig()) {
    process.exit(1);
  }

  await initializeServices();

  const server = createMcpServer({
    authService,
    agentClient,
    onLoginRequested: handleLogin,
  });

  const transport = new StdioServerTransport();
  await server.connect(transport);
}

/**
 * Run in tray mode (default)
 */
async function runTrayMode(): Promise<void> {
  // Don't show dock icon on macOS
  if (process.platform === "darwin") {
    app.dock?.hide();
  }

  // Single instance lock
  const gotLock = app.requestSingleInstanceLock();
  if (!gotLock) {
    console.log("[Main] Another instance is already running");
    app.quit();
    return;
  }

  app.on("second-instance", () => {
    // Focus tray or show notification
    tray?.popUpContextMenu();
  });

  await app.whenReady();

  // Enable Start with Windows by default on first run
  if (!app.getLoginItemSettings().openAtLogin) {
    app.setLoginItemSettings({ openAtLogin: true });
  }

  // Initialize file-based logging
  logger.initialize(process.env.LOG_LEVEL as "debug" | "info" | "warn" | "error" || "info");

  if (!validateConfig()) {
    dialog.showErrorBox(
      "Configuration Error",
      "Missing required environment variables. Please create a .env file with CLIENT_ID, TENANT_ID, and ENVIRONMENT_ID or DIRECT_CONNECT_URL."
    );
    app.quit();
    return;
  }

  try {
    await initializeServices();
    createTray();
    console.log("[Main] Power Agent Tray started");

    // Auto-login if not already authenticated
    const isAuthed = await authService.isAuthenticated();
    if (!isAuthed) {
      console.log("[Main] Not signed in \u2014 launching login flow...");
      try {
        await handleLogin();
      } catch (error) {
        console.error("[Main] Auto-login failed:", error);
        // Non-fatal: user can retry from tray menu
      }
    }

    // Periodic state refresh for tray menu
    setInterval(() => {
      updateTrayMenu();
    }, 30000);
  } catch (error) {
    console.error("[Main] Failed to start:", error);
    dialog.showErrorBox(
      "Startup Error",
      error instanceof Error ? error.message : String(error)
    );
    app.quit();
  }

  // Prevent app from quitting when all windows are closed
  app.on("window-all-closed", () => {
    // No-op: keep running in tray
  });
}

// ───────────────────────────────────────────────────
// Entry Point
// ───────────────────────────────────────────────────
const isStdioMode = process.argv.includes("--stdio");

if (isStdioMode) {
  runStdioMode().catch((error) => {
    console.error("[Main] Stdio mode error:", error);
    process.exit(1);
  });
} else {
  runTrayMode().catch((error) => {
    console.error("[Main] Tray mode error:", error);
    process.exit(1);
  });
}
