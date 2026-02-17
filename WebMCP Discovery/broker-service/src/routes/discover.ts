import { Router } from 'express';
import { BrowserPool } from '../services/browser-pool';
import { discoverWebMCPTools } from '../services/webmcp-reader';

export function createDiscoverRouter(browserPool: BrowserPool): Router {
    const router = Router();

    router.post('/discover', async (req, res) => {
        const { url, waitForSelector, timeout = 30000 } = req.body;

        if (!url) {
            return res.status(400).json({ error: 'URL is required' });
        }

        const startTime = Date.now();
        let instance;

        try {
            instance = await browserPool.acquire();

            // Navigate to page
            await instance.page.goto(url, {
                waitUntil: 'networkidle',
                timeout
            });

            // Wait for specific selector if provided
            if (waitForSelector) {
                await instance.page.waitForSelector(waitForSelector, { timeout });
            }

            // Discover WebMCP tools
            const discovery = await discoverWebMCPTools(instance.page);
            const title = await instance.page.title();

            res.json({
                url,
                title,
                hasWebMCP: discovery.hasWebMCP,
                toolCount: discovery.tools.length,
                tools: discovery.tools,
                serverInfo: discovery.serverInfo,
                scanDurationMs: Date.now() - startTime
            });
        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            
            if (message.includes('timeout')) {
                return res.status(408).json({ error: 'Page load timeout', details: message });
            }
            
            res.status(500).json({ error: 'Discovery failed', details: message });
        } finally {
            if (instance) {
                await browserPool.release(instance.browser);
            }
        }
    });

    return router;
}
