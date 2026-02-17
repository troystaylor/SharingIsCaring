import { Request, Response, NextFunction } from 'express';

/**
 * Managed Identity Authentication
 * Supports Azure AD / Entra ID token validation as alternative to API keys.
 * 
 * Set AUTH_MODE to: 'apikey' | 'managed-identity' | 'both' (default: 'apikey')
 * Set AZURE_TENANT_ID for tenant validation
 * Set AZURE_CLIENT_ID (audience) for token validation
 */

const authMode = process.env.AUTH_MODE || 'apikey';
const tenantId = process.env.AZURE_TENANT_ID || '';
const clientId = process.env.AZURE_CLIENT_ID || '';
const apiKey = process.env.API_KEY || '';

interface TokenClaims {
    iss: string;
    sub: string;
    aud: string;
    exp: number;
    iat: number;
    tid?: string;
    oid?: string;
    appid?: string;
    roles?: string[];
}

function decodeJwtPayload(token: string): TokenClaims | null {
    try {
        const parts = token.split('.');
        if (parts.length !== 3) return null;
        const payload = Buffer.from(parts[1], 'base64url').toString('utf-8');
        return JSON.parse(payload);
    } catch {
        return null;
    }
}

function validateToken(token: string): { valid: boolean; claims?: TokenClaims; error?: string } {
    const claims = decodeJwtPayload(token);
    if (!claims) {
        return { valid: false, error: 'Invalid token format' };
    }

    // Check expiration
    if (claims.exp && claims.exp < Date.now() / 1000) {
        return { valid: false, error: 'Token expired' };
    }

    // Check tenant
    if (tenantId && claims.tid && claims.tid !== tenantId) {
        return { valid: false, error: 'Invalid tenant' };
    }

    // Check audience
    if (clientId && claims.aud !== clientId && claims.aud !== `api://${clientId}`) {
        return { valid: false, error: 'Invalid audience' };
    }

    // Check issuer format
    const validIssuers = [
        `https://login.microsoftonline.com/${tenantId || claims.tid}/v2.0`,
        `https://sts.windows.net/${tenantId || claims.tid}/`
    ];
    if (claims.iss && !validIssuers.some(iss => claims.iss.startsWith(iss.split('/v2.0')[0]))) {
        return { valid: false, error: 'Invalid issuer' };
    }

    return { valid: true, claims };
}

export function managedIdentityMiddleware(req: Request, res: Response, next: NextFunction): void {
    if (req.path === '/health') {
        next();
        return;
    }

    if (authMode === 'apikey') {
        // Traditional API key auth
        const providedKey = req.headers['x-api-key'] as string;
        if (apiKey && providedKey !== apiKey) {
            res.status(401).json({ error: 'Invalid API key' });
            return;
        }
        next();
        return;
    }

    const authHeader = req.headers['authorization'] as string;
    const providedApiKey = req.headers['x-api-key'] as string;

    if (authMode === 'managed-identity') {
        // Only accept Bearer tokens
        if (!authHeader?.startsWith('Bearer ')) {
            res.status(401).json({ error: 'Bearer token required' });
            return;
        }

        const token = authHeader.substring(7);
        const validation = validateToken(token);

        if (!validation.valid) {
            res.status(401).json({ error: validation.error });
            return;
        }

        (req as any).tokenClaims = validation.claims;
        next();
        return;
    }

    if (authMode === 'both') {
        // Accept either Bearer token or API key
        if (authHeader?.startsWith('Bearer ')) {
            const token = authHeader.substring(7);
            const validation = validateToken(token);

            if (validation.valid) {
                (req as any).tokenClaims = validation.claims;
                next();
                return;
            }
        }

        if (providedApiKey && (!apiKey || providedApiKey === apiKey)) {
            next();
            return;
        }

        res.status(401).json({ error: 'Valid Bearer token or API key required' });
        return;
    }

    next();
}
