import { app } from '@azure/functions';
import {
    CopilotStudioClient,
    ConnectionSettings
} from '@microsoft/agents-copilotstudio-client';
import { ConfidentialClientApplication } from '@azure/msal-node';
import crypto from 'node:crypto';

// ── Constants ───────────────────────────────────────────────────────

const SESSION_TTL_MS = 60 * 60 * 1000;          // 1 hour
const MAX_CONV_DURATION_MS = 4 * 60 * 60 * 1000; // 4 hours absolute max
const MAX_AUTH_SESSIONS = 500;
const MAX_CONV_SESSIONS = 1000;
const START_TIMEOUT_MS = 30_000;                  // 30 s for startConversation
const SEND_TIMEOUT_MS = 60_000;                   // 60 s for sendActivity
const startedAt = Date.now();

// ── Session store ───────────────────────────────────────────────────

const authSessions = new Map();
const convSessions = new Map();
const codeToSession = new Map();

// Periodic cleanup (every 10 minutes)
setInterval(() => {
    const now = Date.now();
    for (const [id, s] of authSessions) {
        if (now - s.createdAt > SESSION_TTL_MS) authSessions.delete(id);
    }
    for (const [id, s] of convSessions) {
        if (now - s.createdAt > SESSION_TTL_MS) convSessions.delete(id);
    }
}, 10 * 60 * 1000);

// Evict oldest entries when a Map exceeds its cap
function enforceLimit(map, max) {
    if (map.size <= max) return;
    const sorted = [...map.entries()].sort((a, b) => a[1].createdAt - b[1].createdAt);
    const excess = map.size - max;
    for (let i = 0; i < excess; i++) map.delete(sorted[i][0]);
}

// ── Timeout helper ──────────────────────────────────────────────────

function withTimeout(promise, ms, label) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`${label} timed out after ${ms}ms`)), ms);
        promise.then(
            (v) => { clearTimeout(timer); resolve(v); },
            (e) => { clearTimeout(timer); reject(e); }
        );
    });
}

// ── MSAL Confidential Client ───────────────────────────────────────

const POWER_PLATFORM_SCOPE = 'https://api.powerplatform.com/CopilotStudio.Copilots.Invoke';
const SCOPES = [POWER_PLATFORM_SCOPE, 'offline_access', 'openid'];

let _msalClient = null;

function getMsalClient() {
    if (!_msalClient) {
        _msalClient = new ConfidentialClientApplication({
            auth: {
                clientId: process.env.AZURE_CLIENT_ID,
                clientSecret: process.env.AZURE_CLIENT_SECRET,
                authority: `https://login.microsoftonline.com/${process.env.AZURE_TENANT_ID}`
            }
        });
    }
    return _msalClient;
}

// ── Connection settings ─────────────────────────────────────────────

function getConnectionSettings() {
    const config = {
        appClientId: process.env.AZURE_CLIENT_ID || '',
        tenantId: process.env.AZURE_TENANT_ID || '',
        authority: `https://login.microsoftonline.com/${process.env.AZURE_TENANT_ID}`
    };

    if (process.env.COPILOT_DIRECT_CONNECT_URL) {
        config.directConnectUrl = process.env.COPILOT_DIRECT_CONNECT_URL;
    } else {
        config.environmentId = process.env.COPILOT_ENVIRONMENT_ID || '';
        config.agentIdentifier = process.env.COPILOT_SCHEMA_NAME || '';
    }

    return new ConnectionSettings(config);
}

// ── Helpers ─────────────────────────────────────────────────────────

function extractAgentName(activities) {
    if (!Array.isArray(activities)) return 'Copilot';
    for (const a of activities) {
        if (a.type === 'message' && a.text && a.from?.id !== 'user') {
            const match = a.text.match(/I'm\s+([A-Z][a-zA-Z]+)/);
            if (match) return match[1];
        }
    }
    for (const a of activities) {
        if (a.from?.name && a.from.id !== 'user' && !a.from.name.includes('_')) {
            return a.from.name;
        }
    }
    return 'Copilot';
}

function getBaseUrl() {
    return process.env.MIDDLEWARE_BASE_URL || 'http://localhost:7071';
}

// Resolve a user's access token from their auth session, refreshing if expired.
// Retries once with a 1 s delay on refresh failure before giving up.
async function resolveAccessToken(authSessionId, context) {
    const authSession = authSessions.get(authSessionId);
    if (!authSession) return null;

    if (authSession.expiresAt && Date.now() < authSession.expiresAt - 5 * 60 * 1000) {
        return authSession.accessToken;
    }

    if (authSession.refreshToken) {
        for (let attempt = 0; attempt < 2; attempt++) {
            try {
                const msalClient = getMsalClient();
                const result = await msalClient.acquireTokenByRefreshToken({
                    refreshToken: authSession.refreshToken,
                    scopes: [POWER_PLATFORM_SCOPE]
                });
                authSession.accessToken = result.accessToken;
                authSession.expiresAt = result.expiresOn?.getTime() || (Date.now() + 3600 * 1000);
                context?.log('Token refreshed for session ' + authSessionId);
                return authSession.accessToken;
            } catch (err) {
                context?.warn?.('Token refresh attempt ' + (attempt + 1) + ' failed: ' + err.message);
                if (attempt === 0) await new Promise((r) => setTimeout(r, 1000));
            }
        }
        context?.error('Token refresh failed after 2 attempts for session ' + authSessionId);
        return null;
    }

    return authSession.accessToken;
}

// ── GET /api/auth/login ─────────────────────────────────────────────
// Redirects the user's browser to Entra ID for sign-in.

app.http('authLogin', {
    methods: ['GET'],
    authLevel: 'anonymous',
    route: 'auth/login',
    handler: async (request, context) => {
        const origin = request.query.get('origin') || '';
        const state = crypto.randomBytes(16).toString('hex') + '|' + origin;

        const msalClient = getMsalClient();
        const authCodeUrlParams = {
            scopes: SCOPES,
            redirectUri: getBaseUrl() + '/api/auth/callback',
            state
        };

        // Allow callers to control the prompt behavior:
        // prompt=none → silent SSO (used by automatic re-auth)
        // prompt=select_account → force account picker
        // omitted → Entra ID default (SSO if active session exists)
        const promptParam = request.query.get('prompt');
        if (promptParam) {
            authCodeUrlParams.prompt = promptParam;
        }
        const loginHint = request.query.get('login_hint');
        if (loginHint) {
            authCodeUrlParams.loginHint = loginHint;
        }

        const authCodeUrl = await msalClient.getAuthCodeUrl(authCodeUrlParams);

        return {
            status: 302,
            headers: { Location: authCodeUrl }
        };
    }
});

// ── GET /api/auth/callback ──────────────────────────────────────────
// Handles the OAuth redirect from Entra ID, exchanges code for tokens,
// and posts a message to the parent window (Salesforce LWC popup).

app.http('authCallback', {
    methods: ['GET'],
    authLevel: 'anonymous',
    route: 'auth/callback',
    handler: async (request, context) => {
        const code = request.query.get('code');
        const stateParam = request.query.get('state') || '';
        const error = request.query.get('error');

        if (error) {
            const errorDesc = request.query.get('error_description') || error;
            context.error('OAuth error:', errorDesc);
            return {
                status: 200,
                headers: { 'Content-Type': 'text/html' },
                body: callbackHtml(null, errorDesc)
            };
        }

        if (!code) {
            return {
                status: 400,
                headers: { 'Content-Type': 'text/html' },
                body: callbackHtml(null, 'No authorization code received')
            };
        }

        // Idempotency: if this code was already exchanged, return the same session
        const existingSessionId = codeToSession.get(code);
        if (existingSessionId && authSessions.has(existingSessionId)) {
            context.log('Duplicate callback for already-redeemed code, returning cached session');
            const parts = stateParam.split('|');
            const origin = parts.length > 1 ? parts.slice(1).join('|') : '*';
            return {
                status: 200,
                headers: { 'Content-Type': 'text/html' },
                body: callbackHtml(existingSessionId, null, origin)
            };
        }

        try {
            const msalClient = getMsalClient();
            const tokenResponse = await msalClient.acquireTokenByCode({
                code,
                scopes: [POWER_PLATFORM_SCOPE],
                redirectUri: getBaseUrl() + '/api/auth/callback'
            });

            // Create auth session
            const sessionId = crypto.randomBytes(32).toString('hex');
            authSessions.set(sessionId, {
                accessToken: tokenResponse.accessToken,
                refreshToken: tokenResponse.refreshToken || null,
                expiresAt: tokenResponse.expiresOn?.getTime() || (Date.now() + 3600 * 1000),
                account: tokenResponse.account?.username || '',
                createdAt: Date.now()
            });
            enforceLimit(authSessions, MAX_AUTH_SESSIONS);
            codeToSession.set(code, sessionId);
            // Limit cache size
            if (codeToSession.size > 200) {
                const first = codeToSession.keys().next().value;
                codeToSession.delete(first);
            }

            context.log('Auth session created: ' + sessionId.substring(0, 8) + '...');

            // Extract origin from state
            const parts = stateParam.split('|');
            const origin = parts.length > 1 ? parts.slice(1).join('|') : '*';

            return {
                status: 200,
                headers: { 'Content-Type': 'text/html' },
                body: callbackHtml(sessionId, null, origin)
            };
        } catch (err) {
            context.error('Token exchange failed:', err);
            return {
                status: 200,
                headers: { 'Content-Type': 'text/html' },
                body: callbackHtml(null, 'Token exchange failed: ' + err.message)
            };
        }
    }
});

// HTML page returned by the callback — posts the session ID back to
// the parent window (the Salesforce LWC that opened the popup).
function callbackHtml(sessionId, error, origin) {
    if (error) {
        return `<!DOCTYPE html><html><body>
<h3>Authentication Failed</h3><p>${escapeHtml(error)}</p>
<script>
if(window.opener){window.opener.postMessage({type:'copilot-auth-error',error:${JSON.stringify(error)}},'*');setTimeout(()=>window.close(),2000);}
</script></body></html>`;
    }
    const targetOrigin = origin && origin !== '*' ? JSON.stringify(origin) : "'*'";
    return `<!DOCTYPE html><html><body>
<h3>Signed in successfully. This window will close.</h3>
<script>
if(window.opener){window.opener.postMessage({type:'copilot-auth-success',sessionId:${JSON.stringify(sessionId)}},${targetOrigin});setTimeout(()=>window.close(),1500);}
</script></body></html>`;
}

function escapeHtml(str) {
    return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

// ── Markdown → HTML converter ───────────────────────────────────────
// Lightweight converter for the subset of markdown Copilot Studio emits.
// Handles: headers, bold, italic, links (incl. sf:// scheme),
// unordered/ordered lists, horizontal rules, and paragraphs.
//
// Salesforce record links use a custom sf:// scheme in agent instructions:
//   [Article Title](sf://Knowledge__kav/kA05f000001Y1iiCAC)
// The middleware converts these to relative Lightning URLs:
//   /lightning/r/Knowledge__kav/kA05f000001Y1iiCAC/view

function markdownToHtml(md) {
    if (!md || typeof md !== 'string') return md;
    // If the text already contains HTML block tags, return as-is
    if (/<(?:p|div|ul|ol|li|h[1-6]|br|table)\b/i.test(md)) return md;

    const lines = md.split('\n');
    const out = [];
    let inUl = false;
    let inOl = false;

    function closeLists() {
        if (inUl) { out.push('</ul>'); inUl = false; }
        if (inOl) { out.push('</ol>'); inOl = false; }
    }

    // Convert sf://ObjectApiName/RecordId to /lightning/r/ObjectApiName/RecordId/view
    function resolveSfUrl(url) {
        const sfMatch = url.match(/^sf:\/\/([A-Za-z_]+[A-Za-z0-9_]*)\/([a-zA-Z0-9]{15,18})$/);
        if (sfMatch) return `/lightning/r/${sfMatch[1]}/${sfMatch[2]}/view`;
        return url;
    }

    function inline(text) {
        return text
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')     // **bold**
            .replace(/\*(.+?)\*/g, '<em>$1</em>')                 // *italic*
            .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) => {
                const resolved = resolveSfUrl(url.trim());
                // Block dangerous URL schemes (XSS prevention)
                if (/^(?!https?:\/\/|\/|sf:\/\/)/i.test(resolved)) {
                    return escapeHtml(label);
                }
                // Salesforce record links navigate in-frame; external links open new tab
                if (resolved.startsWith('/lightning/')) {
                    return `<a href="${resolved}" data-record-link="true">${escapeHtml(label)}</a>`;
                }
                return `<a href="${resolved}" target="_blank" rel="noopener">${escapeHtml(label)}</a>`;
            });
    }

    for (const raw of lines) {
        const line = raw.trimEnd();

        // blank line → close lists, insert break
        if (line.trim() === '') {
            closeLists();
            out.push('<br/>');
            continue;
        }

        // headers
        const hMatch = line.match(/^(#{1,4})\s+(.+)/);
        if (hMatch) {
            closeLists();
            const level = Math.min(hMatch[1].length + 2, 6); // # → h3, ## → h4
            out.push(`<h${level}>${inline(hMatch[2])}</h${level}>`);
            continue;
        }

        // horizontal rule
        if (/^[-*_]{3,}\s*$/.test(line)) {
            closeLists();
            out.push('<hr/>');
            continue;
        }

        // unordered list item  (- item  or * item)
        const ulMatch = line.match(/^\s*[-*]\s+(.+)/);
        if (ulMatch) {
            if (inOl) { out.push('</ol>'); inOl = false; }
            if (!inUl) { out.push('<ul>'); inUl = true; }
            out.push(`<li>${inline(ulMatch[1])}</li>`);
            continue;
        }

        // ordered list item  (1. item)
        const olMatch = line.match(/^\s*\d+\.\s+(.+)/);
        if (olMatch) {
            if (inUl) { out.push('</ul>'); inUl = false; }
            if (!inOl) { out.push('<ol>'); inOl = true; }
            out.push(`<li>${inline(olMatch[1])}</li>`);
            continue;
        }

        // regular paragraph text
        closeLists();
        out.push(`<p>${inline(line)}</p>`);
    }

    closeLists();

    // Remove <br/> adjacent to block-level elements and collapse duplicates
    return out.join('\n')
        .replace(/(<br\/>\s*){2,}/g, '<br/>')
        .replace(/(<br\/>\s*)+(<(?:p|h[1-6]|ul|ol|li|hr|div)[ >\/])/gi, '$2')
        .replace(/(<\/(?:p|h[1-6]|ul|ol|li|div)>)\s*(<br\/>\s*)+/gi, '$1')
        .replace(/^(<br\/>\s*)+|(<br\/>\s*)+$/g, '');
}

// Apply markdown conversion to all bot message activities
function convertActivities(activities) {
    if (!Array.isArray(activities)) return activities;
    for (const a of activities) {
        if (a.type === 'message' && a.text && a.from?.id !== 'user') {
            a.text = markdownToHtml(a.text);
        }
    }
    return activities;
}

// ── POST /api/conversations ─────────────────────────────────────────
// Requires header: X-Auth-Session: <sessionId>

app.http('startConversation', {
    methods: ['POST'],
    authLevel: 'function',
    route: 'conversations',
    handler: async (request, context) => {
        const rid = context.invocationId;
        const t0 = Date.now();
        context.log(`[${rid}] POST /api/conversations`);

        const authSessionId = request.headers.get('x-auth-session');
        if (!authSessionId || !authSessions.has(authSessionId)) {
            return { status: 401, jsonBody: { error: 'Not authenticated. Please sign in first.', requestId: rid } };
        }

        try {
            const token = await resolveAccessToken(authSessionId, context);
            if (!token) {
                return { status: 401, jsonBody: { error: 'Session expired. Please sign in again.', requestId: rid } };
            }

            context.log(`[${rid}] token acquired, length=${token.length}`);

            const settings = getConnectionSettings();
            const client = new CopilotStudioClient(settings, token);

            context.log(`[${rid}] calling startConversationAsync...`);
            const rawActivities = await withTimeout(
                client.startConversationAsync(),
                START_TIMEOUT_MS,
                'startConversationAsync'
            );
            const activities = [];
            for await (const activity of rawActivities) {
                activities.push(activity);
            }

            const conversationId = `conv_${Date.now()}_${crypto.randomBytes(4).toString('hex')}`;
            convSessions.set(conversationId, {
                client,
                authSessionId,
                createdAt: Date.now(),
                startedAt: Date.now()
            });
            enforceLimit(convSessions, MAX_CONV_SESSIONS);

            const agentName = extractAgentName(activities);
            convertActivities(activities);
            context.log(`[${rid}] success, activities=${activities.length}, duration=${Date.now() - t0}ms`);

            return {
                status: 200,
                jsonBody: { conversationId, activities, agentName }
            };
        } catch (err) {
            context.error(`[${rid}] startConversation failed (${Date.now() - t0}ms):`, err);
            return {
                status: 500,
                jsonBody: { error: 'Failed to start conversation: ' + err.message, requestId: rid }
            };
        }
    }
});

// ── POST /api/conversations/{conversationId}/activities ─────────────

app.http('sendActivity', {
    methods: ['POST'],
    authLevel: 'function',
    route: 'conversations/{conversationId}/activities',
    handler: async (request, context) => {
        const rid = context.invocationId;
        const t0 = Date.now();
        const { conversationId } = request.params;
        context.log(`[${rid}] POST /api/conversations/${conversationId}/activities`);

        const session = convSessions.get(conversationId);

        if (!session) {
            return { status: 404, jsonBody: { error: 'Conversation not found or expired', requestId: rid } };
        }

        // Enforce absolute max conversation duration
        if (Date.now() - session.startedAt > MAX_CONV_DURATION_MS) {
            convSessions.delete(conversationId);
            return { status: 410, jsonBody: { error: 'Conversation exceeded maximum duration. Please start a new conversation.', requestId: rid } };
        }

        let body;
        try {
            body = await request.json();
        } catch {
            return { status: 400, jsonBody: { error: 'Invalid JSON body', requestId: rid } };
        }

        try {
            const activity = {
                type: 'message',
                text: body.text || '',
                from: { id: 'user', name: 'User' }
            };
            if (body.channelData) {
                activity.channelData = body.channelData;
            }

            const rawActivities = await withTimeout(
                session.client.sendActivity(activity),
                SEND_TIMEOUT_MS,
                'sendActivity'
            );
            const activities = Array.isArray(rawActivities) ? rawActivities : [];
            convertActivities(activities);

            // Extend TTL (but startedAt stays fixed for max-duration enforcement)
            session.createdAt = Date.now();

            context.log(`[${rid}] sendActivity success, activities=${activities.length}, duration=${Date.now() - t0}ms`);
            return { status: 200, jsonBody: { activities } };
        } catch (err) {
            context.error(`[${rid}] sendActivity failed (${Date.now() - t0}ms):`, err);
            return { status: 500, jsonBody: { error: 'Failed to send message: ' + err.message, requestId: rid } };
        }
    }
});

// ── DELETE /api/conversations/{conversationId} ──────────────────────

app.http('endConversation', {
    methods: ['DELETE'],
    authLevel: 'function',
    route: 'conversations/{conversationId}',
    handler: async (request, context) => {
        const { conversationId } = request.params;
        const existed = convSessions.delete(conversationId);
        context.log(`[${context.invocationId}] DELETE conversation ${conversationId}, existed=${existed}`);
        return { status: 200, jsonBody: { ended: true, conversationId } };
    }
});

// ── GET /api/health ─────────────────────────────────────────────────

app.http('health', {
    methods: ['GET'],
    authLevel: 'anonymous',
    route: 'health',
    handler: async () => {
        return {
            status: 200,
            jsonBody: {
                status: 'ok',
                uptime: Math.floor((Date.now() - startedAt) / 1000),
                sessions: {
                    auth: authSessions.size,
                    conversations: convSessions.size
                },
                timestamp: new Date().toISOString()
            }
        };
    }
});
