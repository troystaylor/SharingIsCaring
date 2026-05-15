"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.apiKeyMiddleware = apiKeyMiddleware;
function apiKeyMiddleware(req, res, next) {
    // Skip auth for health check
    if (req.path === '/health') {
        next();
        return;
    }
    const expectedKey = process.env.API_KEY;
    if (!expectedKey) {
        next();
        return;
    }
    const providedKey = req.headers['x-api-key'];
    if (!providedKey || providedKey !== expectedKey) {
        res.status(401).json({ error: 'Invalid or missing API key' });
        return;
    }
    next();
}
//# sourceMappingURL=auth.js.map