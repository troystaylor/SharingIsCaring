import { Request, Response, NextFunction } from 'express';

/**
 * URL Allowlisting middleware
 * Restricts which domains the broker can navigate to.
 * Set ALLOWED_DOMAINS env var as comma-separated list, or leave empty to allow all.
 * Set BLOCKED_DOMAINS env var to block specific domains.
 */

const allowedDomains = (process.env.ALLOWED_DOMAINS || '')
    .split(',')
    .map(d => d.trim().toLowerCase())
    .filter(Boolean);

const blockedDomains = (process.env.BLOCKED_DOMAINS || '')
    .split(',')
    .map(d => d.trim().toLowerCase())
    .filter(Boolean);

// Always block these for security
const HARDCODED_BLOCKED = [
    'localhost',
    '127.0.0.1',
    '0.0.0.0',
    '169.254.169.254',  // Azure IMDS
    'metadata.google.internal',
    '10.0.0.0/8',
];

export function isUrlAllowed(url: string): { allowed: boolean; reason?: string } {
    try {
        const parsed = new URL(url);
        const hostname = parsed.hostname.toLowerCase();

        // Block internal/metadata endpoints (SSRF protection)
        for (const blocked of HARDCODED_BLOCKED) {
            if (hostname === blocked || hostname.startsWith(blocked.replace('/8', ''))) {
                return { allowed: false, reason: `Blocked: internal address ${hostname}` };
            }
        }

        // Check blocked domains
        for (const domain of blockedDomains) {
            if (hostname === domain || hostname.endsWith(`.${domain}`)) {
                return { allowed: false, reason: `Domain ${hostname} is blocked` };
            }
        }

        // If allowlist is configured, only allow listed domains
        if (allowedDomains.length > 0) {
            const isAllowed = allowedDomains.some(
                domain => hostname === domain || hostname.endsWith(`.${domain}`)
            );
            if (!isAllowed) {
                return { allowed: false, reason: `Domain ${hostname} not in allowlist` };
            }
        }

        // Only allow http/https
        if (!['http:', 'https:'].includes(parsed.protocol)) {
            return { allowed: false, reason: `Protocol ${parsed.protocol} not allowed` };
        }

        return { allowed: true };
    } catch {
        return { allowed: false, reason: 'Invalid URL' };
    }
}

export function urlAllowlistMiddleware(req: Request, res: Response, next: NextFunction): void {
    // Check URL in body
    const url = req.body?.url;
    if (url) {
        const check = isUrlAllowed(url);
        if (!check.allowed) {
            res.status(403).json({ error: 'URL blocked by policy', reason: check.reason });
            return;
        }
    }
    next();
}
