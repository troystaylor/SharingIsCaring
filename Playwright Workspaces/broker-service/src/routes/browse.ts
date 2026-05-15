import { Router, Request, Response } from 'express';
import { SessionStore } from '../services/session-store';

/**
 * Browse routes — navigation, interaction, and utility operations
 * on an existing browser session.
 */
export function createBrowseRouter(store: SessionStore): Router {
    const router = Router();

    // Helper: get session or 404
    function getSession(req: Request, res: Response) {
        const session = store.get(req.params.sessionId);
        if (!session) {
            res.status(404).json({ error: 'Session not found or expired' });
            return null;
        }
        return session;
    }

    // POST /api/sessions/:sessionId/navigate
    router.post('/sessions/:sessionId/navigate', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        const start = Date.now();
        try {
            const { url, waitUntil, waitForSelector } = req.body;
            const response = await session.page.goto(url, {
                waitUntil: waitUntil || 'networkidle',
                timeout: 60_000,
            });
            if (waitForSelector) {
                await session.page.waitForSelector(waitForSelector, { timeout: 30_000 });
            }
            session.url = session.page.url();

            store.recordAction(session.id, {
                action: 'navigate', input: { url }, success: true,
                durationMs: Date.now() - start, url: session.url,
            });

            res.json({
                url: session.page.url(),
                title: await session.page.title(),
                status: response?.status() ?? null,
            });
        } catch (err: any) {
            store.recordAction(session.id, {
                action: 'navigate', input: req.body, success: false,
                durationMs: Date.now() - start, url: session.page.url(), error: err.message,
            });
            res.status(408).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/click
    router.post('/sessions/:sessionId/click', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        const start = Date.now();
        try {
            const { selector, text, role, roleName } = req.body;
            const urlBefore = session.page.url();

            if (selector) {
                await session.page.click(selector, { timeout: 30_000 });
            } else if (text) {
                await session.page.getByText(text).click();
            } else if (role) {
                await session.page.getByRole(role as any, { name: roleName }).click();
            } else {
                res.status(400).json({ error: 'Provide selector, text, or role' });
                return;
            }

            await session.page.waitForLoadState('networkidle').catch(() => {});
            const pageChanged = session.page.url() !== urlBefore;

            store.recordAction(session.id, {
                action: 'click', input: req.body, success: true,
                durationMs: Date.now() - start, url: session.page.url(),
            });

            res.json({ success: true, url: session.page.url(), pageChanged });
        } catch (err: any) {
            store.recordAction(session.id, {
                action: 'click', input: req.body, success: false,
                durationMs: Date.now() - start, url: session.page.url(), error: err.message,
            });
            res.status(404).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/type
    router.post('/sessions/:sessionId/type', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        const start = Date.now();
        try {
            const { selector, label, placeholder, text, submit, clear } = req.body;

            let locator;
            if (selector) {
                locator = session.page.locator(selector);
            } else if (label) {
                locator = session.page.getByLabel(label);
            } else if (placeholder) {
                locator = session.page.getByPlaceholder(placeholder);
            } else {
                res.status(400).json({ error: 'Provide selector, label, or placeholder' });
                return;
            }

            if (clear !== false) {
                await locator.fill(text);
            } else {
                await locator.pressSequentially(text);
            }

            if (submit) {
                await locator.press('Enter');
                await session.page.waitForLoadState('networkidle').catch(() => {});
            }

            store.recordAction(session.id, {
                action: 'type', input: { selector, label, placeholder, textLength: text?.length },
                success: true, durationMs: Date.now() - start, url: session.page.url(),
            });

            res.json({ success: true, url: session.page.url() });
        } catch (err: any) {
            store.recordAction(session.id, {
                action: 'type', input: req.body, success: false,
                durationMs: Date.now() - start, url: session.page.url(), error: err.message,
            });
            res.status(404).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/select
    router.post('/sessions/:sessionId/select', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { selector, value, label } = req.body;
            if (value) {
                await session.page.selectOption(selector, { value });
            } else if (label) {
                await session.page.selectOption(selector, { label });
            }
            res.json({ success: true, url: session.page.url() });
        } catch (err: any) {
            res.status(404).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/fill-form
    router.post('/sessions/:sessionId/fill-form', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { fields, submit, submitSelector } = req.body;
            const errors: { field: string; error: string }[] = [];
            let filledCount = 0;

            for (const [field, value] of Object.entries(fields as Record<string, string>)) {
                try {
                    // Try as CSS selector first, then as label
                    if (field.startsWith('#') || field.startsWith('.') || field.startsWith('[')) {
                        await session.page.fill(field, value);
                    } else {
                        await session.page.getByLabel(field).fill(value);
                    }
                    filledCount++;
                } catch (err: any) {
                    errors.push({ field, error: err.message });
                }
            }

            if (submit) {
                if (submitSelector) {
                    await session.page.click(submitSelector);
                } else {
                    await session.page.locator('button[type="submit"], input[type="submit"]').first().click();
                }
                await session.page.waitForLoadState('networkidle').catch(() => {});
            }

            res.json({ success: errors.length === 0, filledFields: filledCount, errors });
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/evaluate
    router.post('/sessions/:sessionId/evaluate', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { script } = req.body;
            const result = await session.page.evaluate(script);
            res.json({ result: JSON.stringify(result) });
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/wait
    router.post('/sessions/:sessionId/wait', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { selector, state, timeout } = req.body;
            await session.page.waitForSelector(selector, {
                state: state || 'visible',
                timeout: timeout || 30_000,
            });
            res.json({ success: true, url: session.page.url() });
        } catch (err: any) {
            res.status(408).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/scroll
    router.post('/sessions/:sessionId/scroll', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { direction, amount, selector } = req.body;
            const pixels = amount || 500;

            if (selector) {
                const element = session.page.locator(selector);
                await element.evaluate((el: Element, opts: { dir: string; px: number }) => {
                    if (opts.dir === 'down') el.scrollTop += opts.px;
                    else if (opts.dir === 'up') el.scrollTop -= opts.px;
                    else if (opts.dir === 'top') el.scrollTop = 0;
                    else if (opts.dir === 'bottom') el.scrollTop = el.scrollHeight;
                }, { dir: direction || 'down', px: pixels });
            } else {
                switch (direction) {
                    case 'up':
                        await session.page.evaluate((px: number) => window.scrollBy(0, -px), pixels);
                        break;
                    case 'top':
                        await session.page.evaluate(() => window.scrollTo(0, 0));
                        break;
                    case 'bottom':
                        await session.page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
                        break;
                    default: // 'down'
                        await session.page.evaluate((px: number) => window.scrollBy(0, px), pixels);
                }
            }

            res.json({ success: true, url: session.page.url() });
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    // GET /api/sessions/:sessionId/screenshot
    router.get('/sessions/:sessionId/screenshot', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const fullPage = req.query.fullPage === 'true';
            const format = (req.query.format as 'png' | 'jpeg') || 'png';
            const selector = req.query.selector as string | undefined;

            let buffer: Buffer;
            if (selector) {
                buffer = await session.page.locator(selector).screenshot({ type: format });
            } else {
                buffer = await session.page.screenshot({ fullPage, type: format });
            }

            const base64 = buffer.toString('base64');

            res.json({
                image: base64,
                format,
                width: session.page.viewportSize()?.width ?? 1280,
                height: session.page.viewportSize()?.height ?? 720,
            });
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    return router;
}
