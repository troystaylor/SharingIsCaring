import { Router, Request, Response } from 'express';
import { BrowserPool } from '../services/browser-pool';
import { SessionStore } from '../services/session-store';

export function createSessionsRouter(pool: BrowserPool, store: SessionStore): Router {
    const router = Router();

    // POST /api/sessions — Create a remote browser session
    router.post('/sessions', async (req: Request, res: Response) => {
        try {
            const { url, ttlMinutes, os, viewport } = req.body;
            const ttl = Math.min(Math.max(ttlMinutes || 15, 1), 60);
            const browserOs = os || process.env.DEFAULT_BROWSER_OS || 'linux';

            // Provision remote browser and connect
            const instance = await pool.acquire(browserOs);

            // Optionally set viewport
            if (viewport?.width && viewport?.height) {
                await instance.page.setViewportSize({
                    width: viewport.width,
                    height: viewport.height,
                });
            }

            // Navigate to start URL if provided
            let title = '';
            let currentUrl = 'about:blank';
            if (url) {
                await instance.page.goto(url, { waitUntil: 'networkidle', timeout: 60_000 });
                title = await instance.page.title();
                currentUrl = instance.page.url();
            }

            const session = store.create(
                instance.browser,
                instance.context,
                instance.page,
                instance.remoteSession,
                currentUrl,
                ttl
            );

            res.status(201).json({
                sessionId: session.id,
                url: currentUrl,
                title,
                expiresAt: session.expiresAt.toISOString(),
                region: instance.remoteSession.region,
            });
        } catch (err: any) {
            const status = err.message?.includes('pool exhausted') ? 429 : 503;
            res.status(status).json({ error: err.message });
        }
    });

    // GET /api/sessions/:sessionId — Get session status
    router.get('/sessions/:sessionId', async (req: Request, res: Response) => {
        const session = store.get(req.params.sessionId);
        if (!session) {
            res.status(404).json({ error: 'Session not found or expired' });
            return;
        }

        let title = '';
        try {
            title = await session.page.title();
        } catch {}

        res.json({
            sessionId: session.id,
            url: session.page.url(),
            title,
            createdAt: session.createdAt.toISOString(),
            expiresAt: session.expiresAt.toISOString(),
            actionCount: session.actionCount,
            region: session.remoteSession.region,
        });
    });

    // DELETE /api/sessions/:sessionId — Close session
    router.delete('/sessions/:sessionId', async (req: Request, res: Response) => {
        const session = store.get(req.params.sessionId);
        if (!session) {
            res.status(404).json({ error: 'Session not found' });
            return;
        }

        await store.close(req.params.sessionId);
        await pool.release(session.browser);
        res.status(204).send();
    });

    return router;
}
