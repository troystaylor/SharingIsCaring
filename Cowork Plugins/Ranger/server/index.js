import express from "express";
import crypto from "crypto";
import path from "path";
import { fileURLToPath } from "url";
import { z } from "zod";
import { registerBrowserTools } from "./tools/browser.js";
import { registerCodeTools } from "./tools/code.js";
import { registerDocumentTools } from "./tools/documents.js";
import { registerMemoryTools } from "./tools/memory.js";
import { registerSessionTools } from "./tools/session.js";
import { registerAutomationTools } from "./tools/automations.js";
import { registerM365Tools } from "./tools/m365.js";
import { startScheduler } from "./tools/automations.js";
import { readFileSync } from "fs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const CHART_WIDGET_HTML = readFileSync(path.join(__dirname, "widgets", "chart.html"), "utf8");
const CLIENT_ID = process.env.OAUTH_CLIENT_ID || "bfc48f06-4caa-4fee-9839-efce4e9ba653";
const TENANT_ID = process.env.OAUTH_TENANT_ID || "common";
const ENTRA_AUDIENCE = process.env.ENTRA_AUDIENCE || "api://auth-e878b5eb-3e12-4c8a-a1ba-3363586bc678/bfc48f06-4caa-4fee-9839-efce4e9ba653";
const ENTRA_CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET || "";
const COWORK_CLIENT_ID = process.env.COWORK_CLIENT_ID || "ranger-cowork-shim";
const COWORK_CLIENT_SECRET = process.env.COWORK_CLIENT_SECRET || "";
const PUBLIC_BASE_URL = process.env.PUBLIC_BASE_URL || "https://powerhoof-mcp.wonderfulbush-78357192.westus2.azurecontainerapps.io";
const ENTRA_SCOPE = process.env.ENTRA_SCOPE || `api://${CLIENT_ID}/access_as_user`;

// ── OAuth Shim Store (in-memory, TTL-based) ──
const STATE_TTL = 600_000;  // 10 min
const CODE_TTL = 300_000;   // 5 min
const pendingStates = new Map(); // shimState → { coworkRedirectUri, coworkState, codeChallenge, codeChallengeMethod, expiresAt }
const issuedCodes = new Map();   // shimCode → { accessToken, expiresIn, coworkRedirectUri, codeChallenge, codeChallengeMethod, expiresAt }

function pruneMap(map) {
    const now = Date.now();
    for (const [k, v] of map) { if (v.expiresAt < now) map.delete(k); }
}

function takeFromMap(map, key) {
    const entry = map.get(key);
    if (!entry) return null;
    map.delete(key);
    if (entry.expiresAt < Date.now()) return null;
    return entry;
}

const app = express();
app.use(express.json());

// CORS for Cowork widget renderer
app.use((req, res, next) => {
    const origin = req.headers.origin || "";
    if (origin.endsWith(".widget-renderer.usercontent.microsoft.com") || origin.includes("cowork.microsoft.com")) {
        res.setHeader("Access-Control-Allow-Origin", origin);
        res.setHeader("Access-Control-Allow-Headers", "Authorization, Content-Type, Accept, mcp-session-id");
        res.setHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        if (req.method === "OPTIONS") return res.status(204).end();
    }
    next();
});

// JWT validation middleware for OAuthPluginVault
async function validateToken(req, res, next) {
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        return res.status(401).json({ error: "Missing or invalid Authorization header" });
    }

    const token = authHeader.slice(7);
    try {
        // Decode token payload (base64url) to extract claims without full JWKS validation
        // In production, use jose or jsonwebtoken with JWKS for full validation
        const payload = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString());

        // Validate audience matches our app (SSO URI, api:// URI, or raw client ID)
        const aud = payload.aud;
        const validAudiences = [CLIENT_ID, `api://${CLIENT_ID}`, ENTRA_AUDIENCE];
        if (!validAudiences.includes(aud)) {
            return res.status(401).json({ error: "Token audience mismatch" });
        }

        // Validate token is not expired
        if (payload.exp && Date.now() / 1000 > payload.exp) {
            return res.status(401).json({ error: "Token expired" });
        }

        // Attach user identity to request for memory/automation scoping
        req.userId = payload.oid || payload.sub || "anonymous";
        req.userEmail = payload.preferred_username || payload.upn || "";
        req.userName = payload.name || "";
        next();
    } catch (e) {
        return res.status(401).json({ error: "Invalid token", details: e.message });
    }
}

// Tool registration is now handled by buildRegistry() below

// ── OAuth Shim: GET /oauth/authorize ──
// Cowork redirects user here. We store the state and redirect to Entra.
app.get("/oauth/authorize", (req, res) => {
    const { client_id, redirect_uri, state, response_type, code_challenge, code_challenge_method } = req.query;
    if (client_id !== COWORK_CLIENT_ID) {
        return res.status(400).json({ error: "invalid_client", message: "Unknown client_id" });
    }
    if (!redirect_uri || !state) {
        return res.status(400).json({ error: "invalid_request", message: "redirect_uri and state required" });
    }

    const shimState = crypto.randomUUID();
    pruneMap(pendingStates);
    pendingStates.set(shimState, {
        coworkRedirectUri: redirect_uri,
        coworkState: state,
        codeChallenge: code_challenge || null,
        codeChallengeMethod: code_challenge_method || null,
        expiresAt: Date.now() + STATE_TTL,
    });

    const entraParams = new URLSearchParams({
        client_id: CLIENT_ID,
        response_type: "code",
        redirect_uri: `${PUBLIC_BASE_URL}/oauth/callback`,
        scope: ENTRA_SCOPE,
        state: shimState,
        response_mode: "query",
    });
    const entraUrl = `https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/authorize?${entraParams}`;
    console.log("OAuth shim: redirecting to Entra authorize");
    res.redirect(entraUrl);
});

// ── OAuth Shim: GET /oauth/callback ──
// Entra redirects here after user signs in. We exchange the code server-side.
app.get("/oauth/callback", async (req, res) => {
    const { code, state: shimState, error, error_description } = req.query;
    if (error) {
        console.error("OAuth shim: Entra error:", error, error_description);
        return res.status(400).json({ error, error_description });
    }
    if (!code || !shimState) {
        return res.status(400).json({ error: "invalid_request", message: "code and state required" });
    }

    const pending = takeFromMap(pendingStates, shimState);
    if (!pending) {
        return res.status(400).json({ error: "invalid_state", message: "Unknown or expired state" });
    }

    // Exchange auth code for tokens with Entra (server-side)
    try {
        const tokenBody = new URLSearchParams({
            client_id: CLIENT_ID,
            client_secret: ENTRA_CLIENT_SECRET,
            code,
            redirect_uri: `${PUBLIC_BASE_URL}/oauth/callback`,
            grant_type: "authorization_code",
            scope: ENTRA_SCOPE,
        });
        const tokenResp = await fetch(`https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token`, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: tokenBody.toString(),
        });
        const tokenData = await tokenResp.json();
        if (!tokenResp.ok || !tokenData.access_token) {
            console.error("OAuth shim: token exchange failed:", tokenData);
            return res.status(502).json({ error: "token_exchange_failed", details: tokenData });
        }

        // Mint a shim code and store the Entra token
        const shimCode = crypto.randomUUID();
        pruneMap(issuedCodes);
        issuedCodes.set(shimCode, {
            accessToken: tokenData.access_token,
            expiresIn: tokenData.expires_in || 3599,
            coworkRedirectUri: pending.coworkRedirectUri,
            codeChallenge: pending.codeChallenge,
            codeChallengeMethod: pending.codeChallengeMethod,
            expiresAt: Date.now() + CODE_TTL,
        });

        // Redirect back to Cowork with the shim code
        const redirectUrl = new URL(pending.coworkRedirectUri);
        redirectUrl.searchParams.set("code", shimCode);
        redirectUrl.searchParams.set("state", pending.coworkState);
        console.log("OAuth shim: redirecting to Cowork with shim code");
        res.redirect(redirectUrl.toString());
    } catch (e) {
        console.error("OAuth shim: token exchange error:", e.message);
        return res.status(502).json({ error: "token_exchange_error", message: e.message });
    }
});

// ── OAuth Shim: POST /oauth/token ──
// Cowork redeems the shim code for the stored Entra token.
app.post("/oauth/token", express.urlencoded({ extended: false }), (req, res) => {
    const { code, grant_type, redirect_uri, client_id, client_secret, code_verifier } = req.body;

    // Also check Authorization header for Basic auth
    let authClientId = client_id;
    let authClientSecret = client_secret;
    const authHeader = req.headers.authorization;
    if (authHeader && authHeader.startsWith("Basic ")) {
        const decoded = Buffer.from(authHeader.slice(6), "base64").toString();
        const [id, secret] = decoded.split(":");
        authClientId = authClientId || id;
        authClientSecret = authClientSecret || secret;
    }

    if (grant_type !== "authorization_code") {
        return res.status(400).json({ error: "unsupported_grant_type" });
    }
    if (!code) {
        return res.status(400).json({ error: "invalid_request", message: "code required" });
    }

    const issued = takeFromMap(issuedCodes, code);
    if (!issued) {
        return res.status(400).json({ error: "invalid_grant", message: "Unknown or expired code" });
    }

    // Validate PKCE if challenge was registered
    if (issued.codeChallenge) {
        if (!code_verifier) {
            return res.status(400).json({ error: "invalid_grant", message: "code_verifier required" });
        }
        const computed = crypto.createHash("sha256").update(code_verifier).digest("base64url");
        if (!crypto.timingSafeEqual(Buffer.from(computed), Buffer.from(issued.codeChallenge))) {
            return res.status(400).json({ error: "invalid_grant", message: "PKCE verification failed" });
        }
    } else {
        // No PKCE — require client secret
        if (authClientId !== COWORK_CLIENT_ID || authClientSecret !== COWORK_CLIENT_SECRET) {
            return res.status(401).json({ error: "invalid_client" });
        }
    }

    console.log("OAuth shim: issuing token to Cowork");
    res.json({
        access_token: issued.accessToken,
        token_type: "Bearer",
        expires_in: issued.expiresIn,
    });
});

// ── Plain JSON-RPC MCP endpoint (matches Cowork's expected format) ──
// Cowork requires application/json responses, not SSE streams.
const toolRegistry = new Map(); // name → { name, description, inputSchema, annotations, handler }

function buildRegistry() {
    const ALLOWED_TOOLS = new Set([
        "create_browser_session", "navigate", "screenshot", "click", "fill", "extract",
        "execute_code", "upload_file", "download_artifact",
        "create_word_doc", "create_excel", "create_powerpoint",
        "save_memory", "recall_memory", "list_memories",
        "create_automation", "list_automations", "destroy_session",
        "save_to_onedrive", "send_email", "create_teams_message",
        "list_calendar_events", "create_calendar_event", "find_free_busy", "delete_calendar_event",
        "read_onedrive_file", "list_onedrive_folder", "delete_onedrive_file",
        "run_parallel"
        // save_to_onedrive re-enabled temporarily for OBO debugging
    ]);

    // Fake server object that captures tool registrations
    const fakeServer = {
        tool(name, ...args) {
            if (!ALLOWED_TOOLS.has(name)) return;
            // Parse overloaded signatures: (name, desc, schema, handler) or (name, desc, schema, annotations, handler) or (name, desc, schema, ann1, ann2, handler)
            let description, schema, annotations, handler;
            if (typeof args[args.length - 1] === "function") {
                handler = args.pop();
            }
            description = typeof args[0] === "string" ? args.shift() : "";
            schema = (args[0] && typeof args[0] === "object" && !args[0].readOnlyHint && !args[0].destructiveHint) ? args.shift() : {};
            annotations = args[0] || {};

            // Convert Zod schema to JSON Schema
            let inputSchema = { type: "object", properties: {} };
            try {
                if (schema && typeof schema === "object") {
                    const props = {};
                    const required = [];
                    for (const [key, val] of Object.entries(schema)) {
                        if (val && val._def) {
                            const def = val._def;
                            const prop = { description: def.description || "" };
                            const innerType = def.innerType?._def?.typeName || def.typeName;
                            if (innerType === "ZodString") prop.type = "string";
                            else if (innerType === "ZodNumber") prop.type = "number";
                            else if (innerType === "ZodBoolean") prop.type = "boolean";
                            else if (innerType === "ZodArray") prop.type = "array";
                            else prop.type = "string";
                            props[key] = prop;
                            if (def.typeName !== "ZodOptional") required.push(key);
                        }
                    }
                    inputSchema = { type: "object", properties: props };
                    if (required.length > 0) inputSchema.required = required;
                }
            } catch (e) { /* fallback to empty schema */ }

            toolRegistry.set(name, { name, description, inputSchema, annotations, handler });
        }
    };

    registerBrowserTools(fakeServer, z);
    registerCodeTools(fakeServer, z);
    registerDocumentTools(fakeServer, z);
    registerMemoryTools(fakeServer, z);
    registerSessionTools(fakeServer, z);
    registerAutomationTools(fakeServer, z);
    registerM365Tools(fakeServer, z);
    console.log(`Registered ${toolRegistry.size} tools in plain JSON-RPC mode`);
}
buildRegistry();

// Add run_parallel tool directly to registry (it calls other tools, so needs registry access)
toolRegistry.set("run_parallel", {
    name: "run_parallel",
    description: "Execute multiple tools in parallel. Returns all results. Use for concurrent research, batch operations, or multi-step workflows.",
    inputSchema: {
        type: "object",
        properties: {
            tasks: {
                type: "array",
                description: "Array of tool calls to execute concurrently",
                items: {
                    type: "object",
                    properties: {
                        tool: { type: "string", description: "Tool name to call" },
                        args: { type: "object", description: "Arguments for the tool" },
                    },
                    required: ["tool", "args"],
                },
            },
        },
        required: ["tasks"],
    },
    annotations: { readOnlyHint: true },
    handler: async ({ tasks, user_id, user_token }) => {
        const results = await Promise.all(tasks.map(async (task, i) => {
            const tool = toolRegistry.get(task.tool);
            if (!tool) return { index: i, tool: task.tool, error: `Unknown tool: ${task.tool}` };
            try {
                const args = { ...task.args };
                if (!args.user_id) args.user_id = user_id;
                if (!args.user_token) args.user_token = user_token;
                const result = await tool.handler(args);
                return { index: i, tool: task.tool, result };
            } catch (e) {
                return { index: i, tool: task.tool, error: e.message };
            }
        }));
        return { content: [{ type: "text", text: JSON.stringify({ parallel: true, count: results.length, results }) }] };
    },
});

app.post("/mcp", validateToken, async (req, res) => {
    const body = req.body;
    const id = body?.id ?? null;
    const method = body?.method;

    console.log("POST /mcp method:", method, "user:", req.userId);

    // Handle notifications (no response needed)
    if (id == null && method?.startsWith("notifications/")) {
        return res.status(204).end();
    }

    try {
        let result;
        switch (method) {
            case "initialize":
                result = {
                    protocolVersion: body?.params?.protocolVersion || "2025-03-26",
                    serverInfo: { name: "ranger", version: "1.0.0" },
                    capabilities: { tools: { listChanged: false }, resources: { listChanged: false } },
                };
                break;

            case "tools/list": {
                // Tools that run without confirmation (readOnlyHint: true)
                const autoRunTools = new Set([
                    "execute_code", "create_browser_session", "navigate", "screenshot",
                    "extract", "get_console_log", "generate_pdf", "run_playwright_script",
                    "recall_memory", "list_memories", "list_automations",
                    "get_session_status", "list_files", "download_artifact",
                    "list_calendar_events", "find_free_busy",
                    "read_onedrive_file", "list_onedrive_folder",
                    "run_parallel",
                ]);
                // Tools that require confirmation (destructive or writes)
                const confirmTools = new Set([
                    "save_memory", "delete_memory", "save_to_onedrive",
                    "send_email", "create_teams_message", "create_automation",
                    "click", "fill", "destroy_session",
                    "create_calendar_event", "delete_calendar_event",
                    "delete_onedrive_file",
                ]);

                const tools = [];
                for (const [, t] of toolRegistry) {
                    const entry = { name: t.name, description: t.description, inputSchema: t.inputSchema };
                    // Override annotations for confirmation control
                    if (autoRunTools.has(t.name)) {
                        entry.annotations = { title: t.annotations?.title || t.name, readOnlyHint: true };
                    } else if (confirmTools.has(t.name)) {
                        entry.annotations = { title: t.annotations?.title || t.name, readOnlyHint: false, destructiveHint: t.name === "delete_memory" || t.name === "destroy_session" };
                    } else {
                        entry.annotations = { title: t.annotations?.title || t.name, readOnlyHint: true };
                    }
                    // Add _meta for widget-enabled tools
                    if (t.name === "execute_code" || t.name === "screenshot") {
                        entry._meta = { "openai/outputTemplate": "ui://widget/chart.html" };
                    }
                    tools.push(entry);
                }
                result = { tools };
                break;
            }

            case "tools/call": {
                const toolName = body?.params?.name;
                const args = body?.params?.arguments || {};
                const tool = toolRegistry.get(toolName);
                if (!tool) {
                    return res.json({ jsonrpc: "2.0", id, error: { code: -32601, message: `unknown tool: ${toolName}` } });
                }
                if (!args.user_id) args.user_id = req.userId;
                // Inject bearer token for M365 tools (OBO flow)
                if (!args.user_token) {
                    const authHeader = req.headers.authorization;
                    if (authHeader) args.user_token = authHeader.slice(7);
                }
                const toolResult = await tool.handler(args);
                result = toolResult;
                break;
            }

            case "resources/list":
                result = {
                    resources: [
                        { uri: "ui://widget/chart.html", name: "Chart Widget", mimeType: "text/html+skybridge" }
                    ]
                };
                break;

            case "resources/read": {
                const uri = body?.params?.uri;
                if (uri === "ui://widget/chart.html") {
                    result = {
                        contents: [{
                            uri: "ui://widget/chart.html",
                            mimeType: "text/html+skybridge",
                            text: CHART_WIDGET_HTML,
                        }]
                    };
                } else {
                    return res.json({ jsonrpc: "2.0", id, error: { code: -32602, message: `unknown resource: ${uri}` } });
                }
                break;
            }

            case "ping":
                result = {};
                break;

            default:
                return res.json({ jsonrpc: "2.0", id, error: { code: -32601, message: `method not found: ${method}` } });
        }

        res.setHeader("Content-Type", "application/json");
        res.json({ jsonrpc: "2.0", id, result });
    } catch (err) {
        console.error("MCP error:", method, err.message);
        res.json({ jsonrpc: "2.0", id, error: { code: -32000, message: err.message } });
    }
});

app.get("/health", (req, res) => res.json({ status: "ok", version: "1.0.0" }));

const port = process.env.PORT || 8080;
app.listen(port, () => {
    console.log(`Ranger MCP server listening on :${port}`);
    startScheduler();
});
