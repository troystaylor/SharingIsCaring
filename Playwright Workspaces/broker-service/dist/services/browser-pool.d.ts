/**
 * Browser Pool — manages connections to Playwright Workspaces remote browsers
 * or local Chromium (fallback for local dev/testing).
 *
 * Mode is controlled by PLAYWRIGHT_SERVICE_URL:
 *   - Set → connects to Azure Playwright Workspaces via CDP WebSocket
 *   - Not set → launches local Chromium (requires `npx playwright install chromium`)
 */
import { Browser, BrowserContext, Page } from 'playwright-core';
import { type RemoteBrowserSession } from './playwright-workspaces-client';
export interface BrowserInstance {
    browser: Browser;
    context: BrowserContext;
    page: Page;
    remoteSession: RemoteBrowserSession;
}
export declare class BrowserPool {
    private activeBrowsers;
    private maxBrowsers;
    private isLocalMode;
    constructor();
    acquire(os?: string): Promise<BrowserInstance>;
    release(browser: Browser): Promise<void>;
    closeAll(): Promise<void>;
    getPoolSize(): number;
}
//# sourceMappingURL=browser-pool.d.ts.map