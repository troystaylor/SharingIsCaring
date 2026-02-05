import { contextBridge, ipcRenderer } from 'electron';

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
  // Open external links
  openExternal: (url: string) => ipcRenderer.invoke('open-external', url),

  // Dialog
  showDialog: (options: Electron.MessageBoxOptions) => ipcRenderer.invoke('show-dialog', options),

  // Safe storage for credentials
  safeStorageEncrypt: (plainText: string) => ipcRenderer.invoke('safe-storage-encrypt', plainText),
  safeStorageDecrypt: (encrypted: string) => ipcRenderer.invoke('safe-storage-decrypt', encrypted),

  // App paths
  getAppPath: () => ipcRenderer.invoke('get-app-path'),

  // Get configuration (Azure credentials)
  getConfig: () => ipcRenderer.invoke('get-config'),

  // Open auth window
  openAuthWindow: (authUrl: string) => ipcRenderer.invoke('open-auth-window', authUrl),

  // Agent communication
  startConversation: (accessToken: string) => ipcRenderer.invoke('start-conversation', accessToken),
  sendMessageToAgent: (message: string, accessToken: string) => ipcRenderer.invoke('send-message-to-agent', message, accessToken),

  // MCP Apps support
  callMcpTool: (toolName: string, args: Record<string, unknown>) => ipcRenderer.invoke('call-mcp-tool', toolName, args),

  // Updates
  checkForUpdates: () => ipcRenderer.invoke('check-for-updates'),

  // Window controls
  minimize: () => ipcRenderer.send('window-minimize'),
  maximize: () => ipcRenderer.send('window-maximize'),
  close: () => ipcRenderer.send('window-close'),

  // Events from main process
  onNewConversation: (callback: () => void) => {
    ipcRenderer.on('new-conversation', callback);
  },
  onProtocolUrl: (callback: (url: string) => void) => {
    ipcRenderer.on('protocol-url', (_event, url) => callback(url));
  }
});

// Type declarations for the renderer
export interface ElectronAPI {
  openExternal: (url: string) => Promise<void>;
  showDialog: (options: Electron.MessageBoxOptions) => Promise<Electron.MessageBoxReturnValue>;
  safeStorageEncrypt: (plainText: string) => Promise<string | null>;
  safeStorageDecrypt: (encrypted: string) => Promise<string | null>;
  getAppPath: () => Promise<string>;
  getConfig: () => Promise<{ clientId: string; tenantId: string; directConnectUrl?: string; speechResourceId?: string; speechRegion?: string }>;
  openAuthWindow: (authUrl: string) => Promise<{ hash: string; search: string; url: string }>;
  startConversation: (accessToken: string) => Promise<{ conversationId: string; agentName?: string }>;
  sendMessageToAgent: (message: string, accessToken: string) => Promise<{ text?: string; cards?: unknown[]; uiResources?: unknown[]; agentName?: string }>;
  callMcpTool: (toolName: string, args: Record<string, unknown>) => Promise<unknown>;
  checkForUpdates: () => Promise<unknown>;
  minimize: () => void;
  maximize: () => void;
  close: () => void;
  onNewConversation: (callback: () => void) => void;
  onProtocolUrl: (callback: (url: string) => void) => void;
}

declare global {
  interface Window {
    electronAPI: ElectronAPI;
  }
}
