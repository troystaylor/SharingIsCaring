import express from 'express';
import { BrowserPool } from './services/browser-pool';
import { SessionStore } from './services/session-store';
import { createDiscoverRouter } from './routes/discover';
import { createSessionsRouter } from './routes/sessions';
import { createExecuteRouter } from './routes/execute';
import { urlAllowlistMiddleware } from './middleware/url-allowlist';
import { auditMiddleware } from './middleware/audit-logger';
import { rbacMiddleware } from './middleware/rbac';
import { managedIdentityMiddleware } from './middleware/managed-identity';

const app = express();
const port = process.env.PORT || 3000;

// Initialize services
const browserPool = new BrowserPool();
const sessionStore = new SessionStore();

// Middleware (order matters)
app.use(express.json());

// 1. Authentication (API key or Managed Identity)
app.use(managedIdentityMiddleware);

// 2. Audit logging (after auth, before processing)
app.use(auditMiddleware);

// 3. RBAC (role-based access control)
app.use(rbacMiddleware);

// 4. URL allowlisting (SSRF protection)
app.use(urlAllowlistMiddleware);

// Routes
app.use('/api', createDiscoverRouter(browserPool));
app.use('/api', createSessionsRouter(browserPool, sessionStore));
app.use('/api', createExecuteRouter(browserPool, sessionStore));

// Health check
app.get('/health', (req, res) => {
    res.json({ 
        status: 'healthy',
        activeSessions: sessionStore.getActiveCount(),
        browserPoolSize: browserPool.getPoolSize()
    });
});

// Graceful shutdown
process.on('SIGTERM', async () => {
    console.log('Shutting down...');
    await sessionStore.closeAll();
    await browserPool.closeAll();
    process.exit(0);
});

app.listen(port, () => {
    console.log(`WebMCP Broker running on port ${port}`);
});
