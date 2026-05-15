"use strict";
/**
 * Browser Pool — manages connections to Playwright Workspaces remote browsers
 * or local Chromium (fallback for local dev/testing).
 *
 * Mode is controlled by PLAYWRIGHT_SERVICE_URL:
 *   - Set → connects to Azure Playwright Workspaces via CDP WebSocket
 *   - Not set → launches local Chromium (requires `npx playwright install chromium`)
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.BrowserPool = void 0;
const playwright_core_1 = require("playwright-core");
const playwright_workspaces_client_1 = require("./playwright-workspaces-client");
class BrowserPool {
    activeBrowsers = [];
    maxBrowsers = parseInt(process.env.MAX_SESSIONS || '10');
    isLocalMode;
    constructor() {
        this.isLocalMode = !process.env.PLAYWRIGHT_SERVICE_URL;
        if (this.isLocalMode) {
            console.log('⚡ LOCAL MODE — launching local Chromium (PLAYWRIGHT_SERVICE_URL not set)');
        }
    }
    async acquire(os = 'linux') {
        if (this.activeBrowsers.length >= this.maxBrowsers) {
            throw new Error(`Browser pool exhausted (max ${this.maxBrowsers}). Close a session first.`);
        }
        let browser;
        let remoteSession;
        if (this.isLocalMode) {
            // Local fallback — launch Chromium directly
            browser = await playwright_core_1.chromium.launch({
                headless: true,
                args: ['--disable-dev-shm-usage', '--no-sandbox'],
            });
            remoteSession = { sessionUrl: 'local', region: 'local', workspaceId: 'local' };
        }
        else {
            // Production — provision remote browser on Playwright Workspaces
            const config = (0, playwright_workspaces_client_1.getConfigFromEnv)();
            remoteSession = await (0, playwright_workspaces_client_1.provisionRemoteBrowser)(config, os);
            browser = await playwright_core_1.chromium.connectOverCDP(remoteSession.sessionUrl, {
                timeout: 60_000,
            });
        }
        this.activeBrowsers.push(browser);
        // Use the default context or create a new one
        const contexts = browser.contexts();
        const context = contexts.length > 0
            ? contexts[0]
            : await browser.newContext({
                viewport: { width: 1280, height: 720 },
                userAgent: 'PlaywrightWorkspacesBroker/1.0',
            });
        const pages = context.pages();
        const page = pages.length > 0 ? pages[0] : await context.newPage();
        return { browser, context, page, remoteSession };
    }
    async release(browser) {
        const index = this.activeBrowsers.indexOf(browser);
        if (index > -1) {
            this.activeBrowsers.splice(index, 1);
        }
        try {
            await browser.close();
        }
        catch {
            // Remote browser may already be destroyed when WebSocket closes
        }
    }
    async closeAll() {
        await Promise.all(this.activeBrowsers.map(b => b.close().catch(() => { })));
        this.activeBrowsers = [];
    }
    getPoolSize() {
        return this.activeBrowsers.length;
    }
}
exports.BrowserPool = BrowserPool;
//# sourceMappingURL=browser-pool.js.map