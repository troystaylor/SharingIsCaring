/**
 * Browser Pool — manages connections to Playwright Workspaces remote browsers
 * or local Chromium (fallback for local dev/testing).
 *
 * Mode is controlled by PLAYWRIGHT_SERVICE_URL:
 *   - Set → connects to Azure Playwright Workspaces via CDP WebSocket
 *   - Not set → launches local Chromium (requires `npx playwright install chromium`)
 */

import { chromium, Browser, BrowserContext, Page } from 'playwright-core';
import { provisionRemoteBrowser, getConfigFromEnv, type RemoteBrowserSession } from './playwright-workspaces-client';

export interface BrowserInstance {
    browser: Browser;
    context: BrowserContext;
    page: Page;
    remoteSession: RemoteBrowserSession;
}

export class BrowserPool {
    private activeBrowsers: Browser[] = [];
    private maxBrowsers = parseInt(process.env.MAX_SESSIONS || '10');
    private isLocalMode: boolean;

    constructor() {
        this.isLocalMode = !process.env.PLAYWRIGHT_SERVICE_URL;
        if (this.isLocalMode) {
            console.log('⚡ LOCAL MODE — launching local Chromium (PLAYWRIGHT_SERVICE_URL not set)');
        }
    }

    async acquire(os: string = 'linux'): Promise<BrowserInstance> {
        if (this.activeBrowsers.length >= this.maxBrowsers) {
            throw new Error(`Browser pool exhausted (max ${this.maxBrowsers}). Close a session first.`);
        }

        let browser: Browser;
        let remoteSession: RemoteBrowserSession;

        if (this.isLocalMode) {
            // Local fallback — launch Chromium directly
            browser = await chromium.launch({
                headless: true,
                args: ['--disable-dev-shm-usage', '--no-sandbox'],
            });
            remoteSession = { sessionUrl: 'local', region: 'local', workspaceId: 'local' };
        } else {
            // Production — provision remote browser on Playwright Workspaces
            const config = getConfigFromEnv();
            remoteSession = await provisionRemoteBrowser(config, os);
            browser = await chromium.connectOverCDP(remoteSession.sessionUrl, {
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

    async release(browser: Browser): Promise<void> {
        const index = this.activeBrowsers.indexOf(browser);
        if (index > -1) {
            this.activeBrowsers.splice(index, 1);
        }
        try {
            await browser.close();
        } catch {
            // Remote browser may already be destroyed when WebSocket closes
        }
    }

    async closeAll(): Promise<void> {
        await Promise.all(this.activeBrowsers.map(b => b.close().catch(() => {})));
        this.activeBrowsers = [];
    }

    getPoolSize(): number {
        return this.activeBrowsers.length;
    }
}
