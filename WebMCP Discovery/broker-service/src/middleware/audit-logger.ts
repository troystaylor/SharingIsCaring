import { Request, Response, NextFunction } from 'express';

/**
 * Audit Logger - sends all actions to console/Azure Monitor
 * 
 * Set AUDIT_LOG_LEVEL to: 'none' | 'basic' | 'detailed' | 'full'
 * Set AZURE_MONITOR_ENDPOINT for Azure Monitor ingestion
 */

export interface AuditEntry {
    timestamp: string;
    correlationId: string;
    action: string;
    method: string;
    path: string;
    sessionId?: string;
    toolName?: string;
    userIp: string;
    apiKeyHash: string;
    statusCode?: number;
    durationMs?: number;
    url?: string;
    error?: string;
    metadata?: Record<string, unknown>;
}

const auditLevel = process.env.AUDIT_LOG_LEVEL || 'basic';
const auditBuffer: AuditEntry[] = [];
const FLUSH_INTERVAL_MS = 10000;
const MAX_BUFFER_SIZE = 100;

// Flush buffer periodically
setInterval(() => flushAuditBuffer(), FLUSH_INTERVAL_MS);

function hashApiKey(key: string): string {
    if (!key) return 'none';
    // Simple hash for logging - not cryptographic
    let hash = 0;
    for (let i = 0; i < key.length; i++) {
        const char = key.charCodeAt(i);
        hash = ((hash << 5) - hash) + char;
        hash |= 0;
    }
    return `key_${Math.abs(hash).toString(16).substring(0, 8)}`;
}

async function flushAuditBuffer(): Promise<void> {
    if (auditBuffer.length === 0) return;

    const entries = auditBuffer.splice(0, auditBuffer.length);
    
    const monitorEndpoint = process.env.AZURE_MONITOR_ENDPOINT;
    if (monitorEndpoint) {
        try {
            const response = await fetch(monitorEndpoint, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(entries)
            });
            if (!response.ok) {
                console.error(`Audit flush failed: ${response.status}`);
            }
        } catch (err) {
            console.error('Audit flush error:', err);
        }
    }
}

export function logAudit(entry: AuditEntry): void {
    if (auditLevel === 'none') return;

    // Always log to console
    const logLine = `[AUDIT] ${entry.timestamp} ${entry.action} ${entry.method} ${entry.path}` +
        (entry.sessionId ? ` session=${entry.sessionId}` : '') +
        (entry.toolName ? ` tool=${entry.toolName}` : '') +
        (entry.statusCode ? ` status=${entry.statusCode}` : '') +
        (entry.durationMs ? ` duration=${entry.durationMs}ms` : '') +
        (entry.error ? ` error=${entry.error}` : '');
    
    console.log(logLine);

    // Buffer for Azure Monitor
    auditBuffer.push(entry);
    if (auditBuffer.length >= MAX_BUFFER_SIZE) {
        flushAuditBuffer();
    }
}

let correlationCounter = 0;

export function auditMiddleware(req: Request, res: Response, next: NextFunction): void {
    if (auditLevel === 'none' || req.path === '/health') {
        next();
        return;
    }

    const startTime = Date.now();
    const correlationId = `${Date.now()}-${++correlationCounter}`;
    
    // Attach correlation ID to request
    (req as any).correlationId = correlationId;

    const entry: AuditEntry = {
        timestamp: new Date().toISOString(),
        correlationId,
        action: 'REQUEST',
        method: req.method,
        path: req.path,
        userIp: req.ip || req.socket.remoteAddress || 'unknown',
        apiKeyHash: hashApiKey(req.headers['x-api-key'] as string || ''),
    };

    // Extract session and tool info
    const sessionMatch = req.path.match(/\/sessions\/([^/]+)/);
    if (sessionMatch) entry.sessionId = sessionMatch[1];

    const toolMatch = req.path.match(/\/tools\/([^/]+)/);
    if (toolMatch) entry.toolName = toolMatch[1];

    if (req.body?.url) entry.url = req.body.url;

    // Log request
    logAudit(entry);

    // Capture response
    const originalJson = res.json.bind(res);
    res.json = function (body: any) {
        const completionEntry: AuditEntry = {
            ...entry,
            action: 'RESPONSE',
            statusCode: res.statusCode,
            durationMs: Date.now() - startTime,
            error: body?.error
        };

        if (auditLevel === 'detailed' || auditLevel === 'full') {
            completionEntry.metadata = {
                toolName: body?.toolName,
                success: body?.success,
                pageChanged: body?.pageChanged
            };
        }

        logAudit(completionEntry);
        return originalJson(body);
    };

    next();
}
