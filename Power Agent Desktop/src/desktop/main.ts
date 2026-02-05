import { app, BrowserWindow, ipcMain, Tray, Menu, nativeImage, shell, dialog, safeStorage } from 'electron';
import { autoUpdater } from 'electron-updater';
import * as path from 'path';
import * as fs from 'fs';
import { spawn, ChildProcess } from 'child_process';
import * as dotenv from 'dotenv';
import { CopilotStudioClient, ConnectionSettings } from '@microsoft/agents-copilotstudio-client';

// Load environment variables from .env file
const envPath = path.join(__dirname, '..', '.env');
if (fs.existsSync(envPath)) {
  dotenv.config({ path: envPath });
} else {
  // Try parent directory (for development)
  dotenv.config({ path: path.join(__dirname, '..', '..', '.env') });
}

// Globals
let mainWindow: BrowserWindow | null = null;
let tray: Tray | null = null;
let mcpServer: ChildProcess | null = null;
let isQuitting = false;

// Copilot Studio client
let copilotClient: CopilotStudioClient | null = null;
let agentName: string = 'Copilot';

// App settings path
const settingsPath = path.join(app.getPath('userData'), 'PowerAgentDesktop');

// Ensure settings directory exists
if (!fs.existsSync(settingsPath)) {
  fs.mkdirSync(settingsPath, { recursive: true });
}

// Create protocol handler for poweragent://
app.setAsDefaultProtocolClient('poweragent');

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1200,
    height: 800,
    minWidth: 600,
    minHeight: 400,
    title: 'Power Agent Desktop',
    icon: path.join(__dirname, '..', 'assets', 'Electron_Software_Framework_Logo.svg.png'),
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    },
    frame: true,
    autoHideMenuBar: true,
    show: false
  });

  // Handle auth popup windows - allow them to open and close properly
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    // Allow Microsoft login popups
    if (url.includes('login.microsoftonline.com') || url.includes('login.live.com')) {
      return {
        action: 'allow',
        overrideBrowserWindowOptions: {
          width: 500,
          height: 650,
          parent: mainWindow!,
          modal: false,
          autoHideMenuBar: true,
          webPreferences: {
            nodeIntegration: false,
            contextIsolation: true
          }
        }
      };
    }
    // Open other links in external browser
    shell.openExternal(url);
    return { action: 'deny' };
  });

  // Load the index.html
  mainWindow.loadFile(path.join(__dirname, 'index.html'));

  // Show when ready
  mainWindow.once('ready-to-show', () => {
    mainWindow?.show();
  });

  // Handle minimize to tray
  mainWindow.on('minimize', () => {
    mainWindow?.hide();
  });

  // Handle close to tray
  mainWindow.on('close', (event) => {
    if (!isQuitting) {
      event.preventDefault();
      mainWindow?.hide();
    }
  });
}

function createTray(): void {
  const trayIconPath = path.join(__dirname, '..', 'assets', 'electron.ico');
  
  // Create a 16x16 icon for the tray
  let trayIcon = nativeImage.createFromPath(trayIconPath);
  if (!trayIcon.isEmpty()) {
    trayIcon = trayIcon.resize({ width: 16, height: 16 });
  }

  tray = new Tray(trayIcon);
  tray.setToolTip('Power Agent Desktop');

  const contextMenu = Menu.buildFromTemplate([
    { 
      label: 'Show App', 
      click: () => {
        mainWindow?.show();
        mainWindow?.focus();
      }
    },
    { type: 'separator' },
    {
      label: 'New Conversation',
      click: () => {
        mainWindow?.webContents.send('new-conversation');
        mainWindow?.show();
      }
    },
    { type: 'separator' },
    { 
      label: 'Quit', 
      click: () => {
        isQuitting = true;
        app.quit();
      }
    }
  ]);

  tray.setContextMenu(contextMenu);

  tray.on('click', () => {
    if (mainWindow?.isVisible()) {
      mainWindow.hide();
    } else {
      mainWindow?.show();
      mainWindow?.focus();
    }
  });
}

function startMcpServer(): void {
  const mcpServerPath = path.join(__dirname, '..', 'mcp-server', 'index.js');
  
  if (fs.existsSync(mcpServerPath)) {
    mcpServer = spawn('node', [mcpServerPath], {
      stdio: 'pipe',
      env: { ...process.env }
    });

    mcpServer.stdout?.on('data', (data: Buffer) => {
      console.log(`MCP Server: ${data.toString()}`);
    });

    mcpServer.stderr?.on('data', (data: Buffer) => {
      console.error(`MCP Server Error: ${data.toString()}`);
    });

    mcpServer.on('close', (code: number | null) => {
      console.log(`MCP Server exited with code ${code}`);
    });
  }
}

// Process activities from Copilot Studio into text and cards
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function processActivities(activities: any[]): { text?: string; cards?: unknown[]; agentName?: string; suggestedActions?: Array<{title: string; value: string}> } {
  const result: { text?: string; cards?: unknown[]; agentName?: string; suggestedActions?: Array<{title: string; value: string}> } = {};
  const textParts: string[] = [];
  const cards: unknown[] = [];
  const suggestedActions: Array<{title: string; value: string}> = [];

  if (!activities) return result;

  console.log('[Activities] Raw activities:', JSON.stringify(activities, null, 2));

  for (const activity of activities) {
    // Extract text messages
    if (activity.type === 'message' && activity.text && activity.from?.id !== 'user') {
      textParts.push(activity.text);
    }

    // Extract agent name
    if (activity.from?.name && activity.from.name !== 'User' && !result.agentName) {
      const name = activity.from.name;
      if (name.includes('_')) {
        const cleaned = name.split('_').pop() || name;
        result.agentName = cleaned
          .replace(/([A-Z])/g, ' $1')
          .replace(/^./, (str: string) => str.toUpperCase())
          .trim();
      } else {
        result.agentName = name;
      }
    }

    // Extract adaptive cards
    if (activity.attachments) {
      for (const attachment of activity.attachments) {
        if (attachment.contentType === 'application/vnd.microsoft.card.adaptive') {
          cards.push(attachment.content);
        }
      }
    }

    // Extract suggested actions
    if (activity.suggestedActions?.actions) {
      for (const action of activity.suggestedActions.actions) {
        if (action.title) {
          suggestedActions.push({
            title: action.title,
            value: action.value || action.title
          });
        }
      }
    }
  }

  if (textParts.length > 0) {
    result.text = textParts.join('\n\n');
    
    // If no explicit suggestedActions, try to parse options from question text
    if (suggestedActions.length === 0) {
      const parsedOptions = parseOptionsFromText(result.text);
      if (parsedOptions.length > 0) {
        for (const option of parsedOptions) {
          suggestedActions.push({ title: option, value: option });
        }
        console.log('[Activities] Parsed options from text:', parsedOptions);
      }
    }
  }
  if (cards.length > 0) {
    result.cards = cards;
  }
  if (suggestedActions.length > 0) {
    result.suggestedActions = suggestedActions;
    console.log('[Activities] Final suggestedActions:', suggestedActions);
  }

  console.log('[Activities] Processed result:', JSON.stringify(result, null, 2));
  return result;
}

/**
 * Parse options from question text like "Would you like X, Y, or Z?"
 * Looks for comma-separated lists ending with "or" in question sentences
 */
function parseOptionsFromText(text: string): string[] {
  console.log('[parseOptionsFromText] Input:', text);
  const options: string[] = [];
  
  // Remove parenthetical content like (e.g., wireless, gaming) to simplify parsing
  const cleanedText = text.replace(/\([^)]+\)/g, '');
  
  // Find all sentences ending with ?
  const questions = cleanedText.match(/[^.!?]*\?/g) || [];
  console.log('[parseOptionsFromText] Found questions:', questions);
  
  for (const question of questions) {
    // Look for "X, Y, or Z" pattern - capture everything between "by/like/between" and the ?
    const listMatch = question.match(/(?:by|like|between|choose|prefer|want)\s+(.+?)\s*\?$/i);
    if (listMatch) {
      const listPart = listMatch[1];
      console.log('[parseOptionsFromText] List part:', listPart);
      
      // Check if it contains "or" (indicating a choice list)
      if (listPart.includes(' or ')) {
        // Split by comma and "or"
        const parts = listPart.split(/,\s*(?:or\s+)?|\s+or\s+/i);
        
        for (const part of parts) {
          let cleaned = part.trim()
            .replace(/\*\*/g, '')  // Remove markdown bold
            .replace(/^(me\s+to\s+|filter\s+by\s+|search\s+for\s+)/i, '') // Remove leading actions
            .trim();
          
          // Skip if too short, too long, or looks like a sentence fragment
          if (cleaned && cleaned.length >= 3 && cleaned.length <= 40 && !cleaned.includes('?')) {
            options.push(cleaned);
          }
        }
      }
    }
    
    // Also look for "Or should I..." pattern as another option
    const orShouldMatch = question.match(/^Or\s+should\s+I\s+(.+?)\s*\?$/i);
    if (orShouldMatch) {
      const action = orShouldMatch[1].trim();
      if (action.length >= 5 && action.length <= 50) {
        // Clean up "help you X" to just "X"
        const cleanAction = action.replace(/^help\s+you\s+/i, '');
        options.push(cleanAction);
      }
    }
  }
  
  console.log('[parseOptionsFromText] Parsed options:', options);
  // Limit to 6 options max
  return options.slice(0, 6);
}

// IPC Handlers
function setupIpcHandlers(): void {
  // Open external links
  ipcMain.handle('open-external', async (_event, url: string) => {
    await shell.openExternal(url);
  });

  // Show dialog
  ipcMain.handle('show-dialog', async (_event, options: Electron.MessageBoxOptions) => {
    return await dialog.showMessageBox(mainWindow!, options);
  });

  // Safe storage - encrypt
  ipcMain.handle('safe-storage-encrypt', (_event, plainText: string) => {
    if (safeStorage.isEncryptionAvailable()) {
      return safeStorage.encryptString(plainText).toString('base64');
    }
    return null;
  });

  // Safe storage - decrypt
  ipcMain.handle('safe-storage-decrypt', (_event, encrypted: string) => {
    if (safeStorage.isEncryptionAvailable()) {
      const buffer = Buffer.from(encrypted, 'base64');
      return safeStorage.decryptString(buffer);
    }
    return null;
  });

  // Get app path
  ipcMain.handle('get-app-path', () => {
    return app.getPath('userData');
  });

  // Get configuration (Azure credentials from .env)
  ipcMain.handle('get-config', () => {
    return {
      clientId: process.env.AZURE_CLIENT_ID || process.env.appClientId || '',
      tenantId: process.env.AZURE_TENANT_ID || process.env.tenantId || '',
      directConnectUrl: process.env.directConnectUrl || '',
      speechResourceId: process.env.AZURE_SPEECH_RESOURCE_ID || '',
      speechRegion: process.env.AZURE_SPEECH_REGION || 'eastus'
    };
  });

  // Start conversation with Copilot Studio
  ipcMain.handle('start-conversation', async (_event, accessToken: string) => {
    try {
      const directConnectUrl = process.env.directConnectUrl;
      
      if (!directConnectUrl) {
        throw new Error('directConnectUrl not configured in .env');
      }

      const settings: ConnectionSettings = {
        directConnectUrl: directConnectUrl
      };

      copilotClient = new CopilotStudioClient(settings, accessToken);
      
      // Start conversation and get initial activities
      const activities = await copilotClient.startConversationAsync();
      
      // Extract agent name from activities
      if (activities && activities.length > 0) {
        for (const activity of activities) {
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const act = activity as any;
          if (act.from?.name && act.from.name !== 'User') {
            const name = act.from.name;
            if (name.includes('_')) {
              const cleaned = name.split('_').pop() || name;
              agentName = cleaned
                .replace(/([A-Z])/g, ' $1')
                .replace(/^./, (str: string) => str.toUpperCase())
                .trim();
            } else {
              agentName = name;
            }
            break;
          }
        }
      }

      // Process initial greeting activities
      const responses = processActivities(activities);
      
      return {
        conversationId: `conv_${Date.now()}`,
        agentName: agentName,
        ...responses
      };
    } catch (error) {
      console.error('Failed to start conversation:', error);
      throw error;
    }
  });

  // Send message to Copilot Studio
  ipcMain.handle('send-message-to-agent', async (_event, message: string, accessToken: string) => {
    try {
      if (!copilotClient) {
        // Auto-initialize if needed
        const directConnectUrl = process.env.directConnectUrl;
        if (!directConnectUrl) {
          throw new Error('directConnectUrl not configured');
        }
        copilotClient = new CopilotStudioClient({ directConnectUrl }, accessToken);
        await copilotClient.startConversationAsync();
      }

      // Send message
      const activity = {
        type: 'message',
        text: message,
        from: { id: 'user', name: 'User' }
      };

      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const activities = await copilotClient.sendActivity(activity as any);
      
      return processActivities(activities);
    } catch (error) {
      console.error('Failed to send message:', error);
      throw error;
    }
  });

  // MCP Apps support - call MCP tools from iframe apps
  ipcMain.handle('call-mcp-tool', async (_event, toolName: string, args: Record<string, unknown>) => {
    try {
      // Route tool calls based on tool name
      switch (toolName) {
        case 'chat_with_agent':
          // Get current access token from renderer and send message
          if (typeof args.message === 'string' && copilotClient) {
            const activity = {
              type: 'message',
              text: args.message,
              from: { id: 'user', name: 'User' }
            };
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const activities = await copilotClient.sendActivity(activity as any);
            return processActivities(activities);
          }
          throw new Error('No active agent connection or invalid message');

        case 'get_agent_capabilities':
          return {
            name: agentName,
            capabilities: ['chat', 'adaptive_cards', 'mcp_apps']
          };

        case 'open_external':
          if (typeof args.url === 'string') {
            await shell.openExternal(args.url);
            return { success: true };
          }
          throw new Error('Invalid URL');

        default:
          // Unknown tool - log and return error
          console.warn(`MCP App called unknown tool: ${toolName}`);
          throw new Error(`Tool '${toolName}' not available`);
      }
    } catch (error) {
      console.error(`MCP tool call failed (${toolName}):`, error);
      throw error;
    }
  });

  // Open auth window and return auth response
  ipcMain.handle('open-auth-window', async (_event, authUrl: string) => {
    return new Promise((resolve, reject) => {
      let resolved = false;
      
      const authWindow = new BrowserWindow({
        width: 500,
        height: 700,
        parent: mainWindow!,
        modal: true,
        autoHideMenuBar: true,
        webPreferences: {
          nodeIntegration: false,
          contextIsolation: true
        }
      });

      // Handle links that auth page tries to open (help, terms, etc)
      authWindow.webContents.setWindowOpenHandler(({ url }) => {
        shell.openExternal(url);
        return { action: 'deny' };
      });

      // Intercept navigation to localhost - this is the redirect with auth code
      authWindow.webContents.on('will-navigate', (event, url) => {
        if (url.startsWith('http://localhost')) {
          event.preventDefault(); // CRITICAL: Prevent the navigation
          if (!resolved) {
            resolved = true;
            const urlObj = new URL(url);
            const hash = urlObj.hash;
            const search = urlObj.search;
            authWindow.close();
            resolve({ hash, search, url });
          }
        }
      });

      // Also check will-redirect
      authWindow.webContents.on('will-redirect', (event, url) => {
        if (url.startsWith('http://localhost')) {
          event.preventDefault(); // CRITICAL: Prevent the redirect
          if (!resolved) {
            resolved = true;
            const urlObj = new URL(url);
            const hash = urlObj.hash;
            const search = urlObj.search;
            authWindow.close();
            resolve({ hash, search, url });
          }
        }
      });

      // Handle page load errors (ignore localhost errors since we intercept them)
      authWindow.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL) => {
        if (validatedURL?.startsWith('http://localhost')) {
          // This is expected - we intercepted the navigation
          return;
        }
        console.error('Auth window failed to load:', errorCode, errorDescription);
        if (!resolved) {
          resolved = true;
          authWindow.close();
          reject(new Error(`Failed to load auth page: ${errorDescription}`));
        }
      });

      // Handle window closed by user
      authWindow.on('closed', () => {
        if (!resolved) {
          reject(new Error('Authentication window was closed'));
        }
      });

      // Load the auth URL
      console.log('Opening auth URL:', authUrl);
      authWindow.loadURL(authUrl).catch(err => {
        console.error('Failed to load auth URL:', err);
        if (!resolved) {
          resolved = true;
          authWindow.close();
          reject(err);
        }
      });
    });
  });

  // Check for updates
  ipcMain.handle('check-for-updates', async () => {
    try {
      const result = await autoUpdater.checkForUpdates();
      return result?.updateInfo;
    } catch (error) {
      console.error('Update check failed:', error);
      return null;
    }
  });

  // Window controls
  ipcMain.on('window-minimize', () => mainWindow?.minimize());
  ipcMain.on('window-maximize', () => {
    if (mainWindow?.isMaximized()) {
      mainWindow.unmaximize();
    } else {
      mainWindow?.maximize();
    }
  });
  ipcMain.on('window-close', () => mainWindow?.close());
}

// App lifecycle
app.whenReady().then(() => {
  createWindow();
  createTray();
  setupIpcHandlers();
  startMcpServer();

  // Check for updates
  autoUpdater.checkForUpdatesAndNotify();

  // Handle auth popup windows - close them when auth redirects back
  app.on('browser-window-created', (_event, window) => {
    // Skip the main window
    if (window === mainWindow) return;
    
    // Monitor navigation in child windows (auth popups)
    window.webContents.on('will-navigate', (_navEvent, url) => {
      // If navigating to localhost (our redirect URI), auth is complete
      if (url.startsWith('http://localhost')) {
        // Let the auth flow complete, then close
        setTimeout(() => {
          if (!window.isDestroyed()) {
            window.close();
          }
        }, 500);
      }
    });

    // Also check when page finishes loading
    window.webContents.on('did-navigate', (_navEvent, url) => {
      if (url.startsWith('http://localhost')) {
        setTimeout(() => {
          if (!window.isDestroyed()) {
            window.close();
          }
        }, 500);
      }
    });
  });

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    // Don't quit, stay in tray
  }
});

app.on('before-quit', () => {
  isQuitting = true;
  mcpServer?.kill();
});

// Handle protocol URLs
app.on('open-url', (_event, url) => {
  console.log('Protocol URL:', url);
  mainWindow?.webContents.send('protocol-url', url);
});
