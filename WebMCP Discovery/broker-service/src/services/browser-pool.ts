import { chromium, Browser, BrowserContext, Page, Route } from 'playwright';
import { isUrlAllowed } from '../middleware/url-allowlist';
import { getRedactionCSS } from '../middleware/data-redaction';

export interface BrowserInstance {
    browser: Browser;
    context: BrowserContext;
    page: Page;
}

export class BrowserPool {
    private browsers: Browser[] = [];
    private maxBrowsers = parseInt(process.env.MAX_BROWSERS || '5');
    private egressEnabled = process.env.NETWORK_EGRESS_CONTROL !== 'false';

    async acquire(): Promise<BrowserInstance> {
        if (this.browsers.length >= this.maxBrowsers) {
            throw new Error('Browser pool exhausted');
        }

        // Launch new browser with optimized settings for container
        const browser = await chromium.launch({
            headless: true,
            args: [
                '--disable-dev-shm-usage',  // Use /tmp instead of /dev/shm
                '--no-sandbox',
                '--disable-setuid-sandbox',
                '--disable-gpu',
                '--disable-software-rasterizer'
            ]
        });

        this.browsers.push(browser);

        const context = await browser.newContext({
            viewport: { width: 1280, height: 720 },
            userAgent: 'WebMCP-Broker/1.0 (+https://webmachinelearning.github.io/webmcp/)'
        });

        // Network egress control: block requests to non-allowed domains
        if (this.egressEnabled) {
            await context.route('**/*', (route: Route) => {
                const requestUrl = route.request().url();
                const check = isUrlAllowed(requestUrl);
                if (check.allowed) {
                    route.continue();
                } else {
                    route.abort('blockedbyclient');
                }
            });
        }

        const page = await context.newPage();

        // Inject redaction CSS to blur sensitive fields in screenshots
        await page.addStyleTag({ content: getRedactionCSS() });

        return { browser, context, page };
    }

    async release(browser: Browser): Promise<void> {
        const index = this.browsers.indexOf(browser);
        if (index > -1) {
            this.browsers.splice(index, 1);
        }
        await browser.close();
    }

    async closeAll(): Promise<void> {
        await Promise.all(this.browsers.map(b => b.close()));
        this.browsers = [];
    }

    getPoolSize(): number {
        return this.browsers.length;
    }
}
