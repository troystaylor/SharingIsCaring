/**
 * Playwright Workspaces Client
 *
 * Provisions remote Chromium browsers on Azure Playwright Workspaces via the
 * data-plane REST API. Returns a CDP WebSocket URL that playwright-core can
 * connect to with chromium.connectOverCDP().
 *
 * No local browser installation is needed — all browsers run in Azure.
 */
export interface PlaywrightWorkspacesConfig {
    serviceUrl: string;
    accessToken: string;
}
export interface RemoteBrowserSession {
    sessionUrl: string;
    region: string;
    workspaceId: string;
}
/**
 * Provision a remote Chromium browser on Playwright Workspaces.
 * Cold start is 30-90s on first call; subsequent calls are faster.
 */
export declare function provisionRemoteBrowser(config: PlaywrightWorkspacesConfig, os?: string): Promise<RemoteBrowserSession>;
/**
 * Build config from environment variables.
 */
export declare function getConfigFromEnv(): PlaywrightWorkspacesConfig;
//# sourceMappingURL=playwright-workspaces-client.d.ts.map