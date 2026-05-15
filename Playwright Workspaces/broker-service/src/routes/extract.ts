import { Router, Request, Response } from 'express';
import { SessionStore } from '../services/session-store';

/**
 * Extract routes — data extraction operations (page content, element text, structured scraping)
 */
export function createExtractRouter(store: SessionStore): Router {
    const router = Router();

    function getSession(req: Request, res: Response) {
        const session = store.get(req.params.sessionId);
        if (!session) {
            res.status(404).json({ error: 'Session not found or expired' });
            return null;
        }
        return session;
    }

    // GET /api/sessions/:sessionId/content
    router.get('/sessions/:sessionId/content', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const format = (req.query.format as string) || 'text';
            const selector = req.query.selector as string | undefined;
            const page = session.page;

            let content: string;

            if (selector) {
                const element = page.locator(selector);
                if (format === 'html') {
                    content = await element.innerHTML();
                } else {
                    content = await element.innerText();
                }
            } else if (format === 'html') {
                content = await page.content();
            } else if (format === 'markdown') {
                // Simple HTML-to-text with structure preserved
                content = await page.evaluate(() => {
                    const body = document.body.cloneNode(true) as HTMLElement;
                    // Remove script/style tags
                    body.querySelectorAll('script, style, noscript').forEach(el => el.remove());
                    return body.innerText;
                });
            } else {
                content = await page.evaluate(() => {
                    const body = document.body.cloneNode(true) as HTMLElement;
                    body.querySelectorAll('script, style, noscript').forEach(el => el.remove());
                    return body.innerText;
                });
            }

            res.json({
                url: page.url(),
                title: await page.title(),
                content,
                format,
            });
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/element-text
    router.post('/sessions/:sessionId/element-text', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { selector, all, attribute } = req.body;
            const page = session.page;

            if (all) {
                const locators = page.locator(selector);
                const count = await locators.count();
                const texts: string[] = [];

                for (let i = 0; i < count; i++) {
                    if (attribute) {
                        const val = await locators.nth(i).getAttribute(attribute);
                        texts.push(val ?? '');
                    } else {
                        texts.push(await locators.nth(i).innerText());
                    }
                }

                res.json({ texts, count, text: texts[0] ?? '' });
            } else {
                const locator = page.locator(selector).first();
                let text: string;

                if (attribute) {
                    text = (await locator.getAttribute(attribute)) ?? '';
                } else {
                    text = await locator.innerText();
                }

                res.json({ text, count: 1 });
            }
        } catch (err: any) {
            res.status(404).json({ error: err.message });
        }
    });

    // POST /api/sessions/:sessionId/scrape
    router.post('/sessions/:sessionId/scrape', async (req, res) => {
        const session = getSession(req, res);
        if (!session) return;

        try {
            const { containerSelector, fields, limit } = req.body;
            const page = session.page;
            const maxItems = Math.min(limit || 50, 200);

            if (containerSelector) {
                // Multi-item scraping: find all containers, extract fields from each
                const items = await page.evaluate(
                    ({ containerSel, fieldMap, max }: { containerSel: string; fieldMap: Record<string, string>; max: number }) => {
                        const containers = document.querySelectorAll(containerSel);
                        const results: Record<string, string>[] = [];

                        for (let i = 0; i < Math.min(containers.length, max); i++) {
                            const container = containers[i];
                            const item: Record<string, string> = {};

                            for (const [name, sel] of Object.entries(fieldMap)) {
                                // Support @attr syntax (e.g., "a@href")
                                const [cssSelector, attr] = sel.split('@');
                                const el = container.querySelector(cssSelector);

                                if (el) {
                                    item[name] = attr
                                        ? el.getAttribute(attr) ?? ''
                                        : el.textContent?.trim() ?? '';
                                } else {
                                    item[name] = '';
                                }
                            }

                            results.push(item);
                        }

                        return results;
                    },
                    { containerSel: containerSelector, fieldMap: fields, max: maxItems }
                );

                res.json({
                    items,
                    count: items.length,
                    url: page.url(),
                });
            } else {
                // Single-item scraping: extract each field from the full page
                const item = await page.evaluate(
                    (fieldMap: Record<string, string>) => {
                        const result: Record<string, string> = {};

                        for (const [name, sel] of Object.entries(fieldMap)) {
                            const [cssSelector, attr] = sel.split('@');
                            const el = document.querySelector(cssSelector);

                            if (el) {
                                result[name] = attr
                                    ? el.getAttribute(attr) ?? ''
                                    : el.textContent?.trim() ?? '';
                            } else {
                                result[name] = '';
                            }
                        }

                        return result;
                    },
                    fields
                );

                res.json({
                    items: [item],
                    count: 1,
                    url: page.url(),
                });
            }
        } catch (err: any) {
            res.status(500).json({ error: err.message });
        }
    });

    return router;
}
