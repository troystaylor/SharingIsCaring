"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const browser_pool_1 = require("./services/browser-pool");
const session_store_1 = require("./services/session-store");
const sessions_1 = require("./routes/sessions");
const browse_1 = require("./routes/browse");
const extract_1 = require("./routes/extract");
const auth_1 = require("./middleware/auth");
const app = (0, express_1.default)();
const port = process.env.PORT || 3000;
// Initialize services
const browserPool = new browser_pool_1.BrowserPool();
const sessionStore = new session_store_1.SessionStore();
// Middleware
app.use(express_1.default.json({ limit: '1mb' }));
app.use(auth_1.apiKeyMiddleware);
// Routes
app.use('/api', (0, sessions_1.createSessionsRouter)(browserPool, sessionStore));
app.use('/api', (0, browse_1.createBrowseRouter)(sessionStore));
app.use('/api', (0, extract_1.createExtractRouter)(sessionStore));
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
//# sourceMappingURL=index.js.map