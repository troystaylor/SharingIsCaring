import express from 'express';
import { BrowserPool } from './services/browser-pool';
import { SessionStore } from './services/session-store';
import { createSessionsRouter } from './routes/sessions';
import { createBrowseRouter } from './routes/browse';
import { createExtractRouter } from './routes/extract';
import { apiKeyMiddleware } from './middleware/auth';

const app = express();
const port = process.env.PORT || 3000;

// Initialize services
const browserPool = new BrowserPool();
const sessionStore = new SessionStore();

// Middleware
app.use(express.json({ limit: '1mb' }));
app.use(apiKeyMiddleware);

// Routes
app.use('/api', createSessionsRouter(browserPool, sessionStore));
app.use('/api', createBrowseRouter(sessionStore));
app.use('/api', createExtractRouter(sessionStore));

// Health check (no auth required — handled before middleware)
app.get('/health', (_req, res) => {
    res.json({
        status: 'healthy',
        activeSessions: sessionStore.getActiveCount(),
        browserPoolSize: browserPool.getPoolSize(),
    });
});

// Graceful shutdown
process.on('SIGTERM', async () => {
    console.log('Shutting down — closing all remote browser sessions...');
    await sessionStore.closeAll();
    await browserPool.closeAll();
    process.exit(0);
});

app.listen(port, () => {
    console.log(`Playwright Workspaces Broker running on port ${port}`);
    console.log(`Service URL configured: ${process.env.PLAYWRIGHT_SERVICE_URL ? 'Yes' : 'NO — set PLAYWRIGHT_SERVICE_URL'}`);
});
