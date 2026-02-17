import { Router } from 'express';
import { BrowserPool } from '../services/browser-pool';
import { SessionStore } from '../services/session-store';
import { discoverWebMCPTools, callWebMCPTool } from '../services/webmcp-reader';
import { executePlaywrightTool } from '../services/playwright-executor';
import { redactObject } from '../middleware/data-redaction';

const redactionEnabled = process.env.REDACTION_FIELDS !== '';

export function createExecuteRouter(
    browserPool: BrowserPool,
    sessionStore: SessionStore
): Router {
    const router = Router();

    // Execute tool in session
    router.post('/sessions/:sessionId/tools/:toolName/call', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);

        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const toolName = req.params.toolName;
        const { input = {}, timeout = 30000 } = req.body;
        const startTime = Date.now();

        try {
            session.callCount++;
            const originalUrl = session.page.url();

            // Check if page has WebMCP
            const discovery = await discoverWebMCPTools(session.page);
            let result;
            let isError = false;

            if (discovery.hasWebMCP) {
                // Try WebMCP tool first
                const webmcpTool = discovery.tools.find(t => t.name === toolName);
                
                if (webmcpTool) {
                    result = await callWebMCPTool(session.page, toolName, input);
                } else if (toolName.startsWith('browser_')) {
                    // Fall back to Playwright for browser_* tools
                    const pwResult = await executePlaywrightTool(session.page, toolName, input, session.context);
                    result = pwResult.result;
                    isError = !pwResult.success;
                    if (isError) {
                        sessionStore.recordAction(session.id, {
                            toolName, input, success: false,
                            durationMs: Date.now() - startTime,
                            url: session.page.url(),
                            error: pwResult.error
                        });
                        return res.status(500).json({
                            toolName,
                            success: false,
                            error: pwResult.error,
                            executionTimeMs: Date.now() - startTime
                        });
                    }
                } else {
                    return res.status(404).json({ error: `Tool '${toolName}' not found` });
                }
            } else {
                // No WebMCP - use Playwright tools only
                if (!toolName.startsWith('browser_')) {
                    return res.status(404).json({
                        error: `Tool '${toolName}' not found. Page has no WebMCP. Available tools: browser_navigate, browser_click, browser_type, etc.`
                    });
                }

                const pwResult = await executePlaywrightTool(session.page, toolName, input, session.context);
                result = pwResult.result;
                
                if (!pwResult.success) {
                    sessionStore.recordAction(session.id, {
                        toolName, input, success: false,
                        durationMs: Date.now() - startTime,
                        url: session.page.url(),
                        error: pwResult.error
                    });
                    return res.status(500).json({
                        toolName,
                        success: false,
                        error: pwResult.error,
                        executionTimeMs: Date.now() - startTime
                    });
                }
            }

            const durationMs = Date.now() - startTime;
            const newUrl = session.page.url();
            const pageChanged = newUrl !== originalUrl;

            // Record action for compliance
            sessionStore.recordAction(session.id, {
                toolName, input, success: true,
                durationMs, url: newUrl
            });

            // Redact sensitive data from results
            const safeResult = redactionEnabled ? redactObject(result) : result;

            res.json({
                toolName,
                success: true,
                result: safeResult,
                executionTimeMs: durationMs,
                pageChanged,
                newUrl: pageChanged ? newUrl : undefined
            });
        } catch (error) {
            const durationMs = Date.now() - startTime;
            const errorMsg = error instanceof Error ? error.message : String(error);

            sessionStore.recordAction(session.id, {
                toolName, input, success: false,
                durationMs, url: session.page.url(),
                error: errorMsg
            });

            res.status(500).json({
                toolName,
                success: false,
                error: errorMsg,
                executionTimeMs: durationMs
            });
        }
    });

    // Get session recording
    router.get('/sessions/:sessionId/recording', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);
        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        res.json({
            sessionId: session.id,
            recordingEnabled: session.recordingEnabled,
            actionCount: session.recording.length,
            actions: session.recording
        });
    });

    // Toggle session recording
    router.post('/sessions/:sessionId/recording', async (req, res) => {
        const session = sessionStore.get(req.params.sessionId);
        if (!session) {
            return res.status(404).json({ error: 'Session not found or expired' });
        }

        const { enabled } = req.body;
        sessionStore.setRecording(session.id, enabled !== false);
        res.json({ recordingEnabled: enabled !== false });
    });

    // Stateless single-shot execution
    router.post('/execute', async (req, res) => {
        const { url, toolName, input = {}, timeout = 60000 } = req.body;

        if (!url || !toolName) {
            return res.status(400).json({ error: 'URL and toolName are required' });
        }

        const startTime = Date.now();
        let instance;

        try {
            instance = await browserPool.acquire();

            // Navigate to page
            await instance.page.goto(url, {
                waitUntil: 'networkidle',
                timeout: timeout / 2  // Half timeout for navigation
            });

            const originalUrl = instance.page.url();

            // Discover tools
            const discovery = await discoverWebMCPTools(instance.page);
            let result;

            if (discovery.hasWebMCP) {
                const webmcpTool = discovery.tools.find(t => t.name === toolName);
                
                if (webmcpTool) {
                    result = await callWebMCPTool(instance.page, toolName, input);
                } else if (toolName.startsWith('browser_')) {
                    const pwResult = await executePlaywrightTool(instance.page, toolName, input, instance.context);
                    if (!pwResult.success) {
                        return res.status(500).json({
                            toolName,
                            success: false,
                            error: pwResult.error,
                            executionTimeMs: Date.now() - startTime
                        });
                    }
                    result = pwResult.result;
                } else {
                    return res.status(404).json({ error: `Tool '${toolName}' not found on page` });
                }
            } else {
                if (!toolName.startsWith('browser_')) {
                    return res.status(404).json({
                        error: `Page has no WebMCP. Use browser_* tools instead.`,
                        availableTools: discovery.tools.map(t => t.name)
                    });
                }

                const pwResult = await executePlaywrightTool(instance.page, toolName, input, instance.context);
                if (!pwResult.success) {
                    return res.status(500).json({
                        toolName,
                        success: false,
                        error: pwResult.error,
                        executionTimeMs: Date.now() - startTime
                    });
                }
                result = pwResult.result;
            }

            const newUrl = instance.page.url();
            const pageChanged = newUrl !== originalUrl;

            res.json({
                toolName,
                success: true,
                result,
                executionTimeMs: Date.now() - startTime,
                pageChanged,
                newUrl: pageChanged ? newUrl : undefined
            });
        } catch (error) {
            const message = error instanceof Error ? error.message : String(error);
            
            if (message.includes('timeout')) {
                return res.status(408).json({ error: 'Execution timeout', details: message });
            }

            res.status(500).json({
                toolName,
                success: false,
                error: message,
                executionTimeMs: Date.now() - startTime
            });
        } finally {
            if (instance) {
                await browserPool.release(instance.browser);
            }
        }
    });

    return router;
}
