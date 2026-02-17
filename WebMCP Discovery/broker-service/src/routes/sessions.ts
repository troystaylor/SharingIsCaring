import { Router } from 'express';
import { BrowserPool } from '../services/browser-pool';
import { SessionStore } from '../services/session-store';
import { discoverWebMCPTools } from '../services/webmcp-reader';

export function createSessionsRouter(
    browserPool: BrowserPool,
    sessionStore: SessionStore
): Router {
    const router = Router();

    // Create session
    router.post('/sessions', async (req, res) => {
        const { url, ttlMinutes = 15, viewport, userAgent } = req.body;

        if (!url) {
            return res.status(400).json({ error: 'URL is required' });
        }

        const clampedTtl = Math.min(Math.max(ttlMinutes, 1), 60);

        try {
            const instance = await browserPool.acquire();

            // Configure viewport if specified
            if (viewport?.width && viewport?.height) {
                await instance.page.setViewportSize(viewport);
            }

            // Configure user agent if specified
            if (userAgent) {
                await instance.page.route('**/*', (route) =>
                    route.continue({ headers: { ...route.request().headers(), 'user-agent': userAgent } })
                );
            }

            // Navigate to initial URL
            await instance.page.goto(url, { waitUntil: 'networkidle' });

            // Create session
            const session = sessionStore.create(
                instance.browser,
                instance.context,
                instance.page,
                url,
                clampedTtl
            );

            // Discover initial tools
            const discovery = await discoverWebMCPTools(instance.page);
            session.hasWebMCP = discovery.hasWebMCP;

            res.status(201).json({
                sessionId: session.id,
                url: session.url,
                expiresAt: session.expiresAt.toISOString(),
                hasWebMCP: discovery.hasWebMCP,
                tools: discovery.tools
            });
        } catch (error) {
            res.status(500).json({
                error: 'Failed to create session',
                details: error instanceof Error ? error.message : String(error)
            });
        }
    });

    // Get session status
    router.get('/sessions/:sessionId', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const title = await session.page.title().catch(() => '');
        const discovery = await discoverWebMCPTools(session.page);

        res.json({
            sessionId: session.id,
            status: 'active',
            url: session.page.url(),
            title,
            expiresAt: session.expiresAt.toISOString(),
            remainingSeconds: Math.max(0, Math.floor((session.expiresAt.getTime() - Date.now()) / 1000)),
            toolCount: discovery.tools.length,
            hasWebMCP: discovery.hasWebMCP,
            callCount: session.callCount
        });
    });

    // Close session
    router.delete('/sessions/:sessionId', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found' });
        }

        await sessionStore.close(session.id);
        res.status(204).send();
    });

    // Navigate within session
    router.post('/sessions/:sessionId/navigate', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const { url, waitForSelector } = req.body;

        if (!url) {
            return res.status(400).json({ error: 'URL is required' });
        }

        try {
            await session.page.goto(url, { waitUntil: 'networkidle' });

            if (waitForSelector) {
                await session.page.waitForSelector(waitForSelector);
            }

            const title = await session.page.title();
            const discovery = await discoverWebMCPTools(session.page);
            session.hasWebMCP = discovery.hasWebMCP;
            session.url = url;

            res.json({
                url: session.page.url(),
                title,
                hasWebMCP: discovery.hasWebMCP,
                tools: discovery.tools
            });
        } catch (error) {
            res.status(408).json({
                error: 'Navigation failed',
                details: error instanceof Error ? error.message : String(error)
            });
        }
    });

    // List session tools
    router.get('/sessions/:sessionId/tools', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const discovery = await discoverWebMCPTools(session.page);

        res.json({
            url: session.page.url(),
            hasWebMCP: discovery.hasWebMCP,
            tools: discovery.tools
        });
    });

    // Inject authentication
    router.post('/sessions/:sessionId/authenticate', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const { cookies, localStorage, sessionStorage, headers } = req.body;

        try {
            // Inject cookies
            if (cookies && Array.isArray(cookies)) {
                await session.context.addCookies(cookies);
            }

            // Inject localStorage
            if (localStorage && typeof localStorage === 'object') {
                await session.page.evaluate((items) => {
                    for (const [key, value] of Object.entries(items)) {
                        window.localStorage.setItem(key, String(value));
                    }
                }, localStorage);
            }

            // Inject sessionStorage
            if (sessionStorage && typeof sessionStorage === 'object') {
                await session.page.evaluate((items) => {
                    for (const [key, value] of Object.entries(items)) {
                        window.sessionStorage.setItem(key, String(value));
                    }
                }, sessionStorage);
            }

            // Set extra HTTP headers
            if (headers && typeof headers === 'object') {
                await session.context.setExtraHTTPHeaders(headers);
            }

            res.json({ success: true });
        } catch (error) {
            res.status(500).json({
                error: 'Failed to inject authentication',
                details: error instanceof Error ? error.message : String(error)
            });
        }
    });

    // Take screenshot
    router.get('/sessions/:sessionId/screenshot', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const fullPage = req.query.fullPage === 'true';
        const format = (req.query.format as 'png' | 'jpeg') || 'png';

        try {
            const screenshot = await session.page.screenshot({
                fullPage,
                type: format
            });

            const viewport = session.page.viewportSize();

            res.json({
                format,
                width: viewport?.width,
                height: viewport?.height,
                base64: screenshot.toString('base64')
            });
        } catch (error) {
            res.status(500).json({
                error: 'Screenshot failed',
                details: error instanceof Error ? error.message : String(error)
            });
        }
    });

    return router;
}
