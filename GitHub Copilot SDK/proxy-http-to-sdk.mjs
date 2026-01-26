#!/usr/bin/env node
// HTTP JSON-RPC server bridging to GitHub Copilot SDK (CopilotClient)
// Usage:
//   PORT=3000 node proxy-http-to-sdk.mjs
//   Cloudflare Tunnel --url http://localhost:3000
//   Update DefaultSdkUrl in script.csx with the tunnel URL + /jsonrpc

import http from 'http';
import { CopilotClient } from '@github/copilot-sdk';

const PORT = process.env.PORT || 3000;
const MAX_BODY = parseInt(process.env.MAX_BODY || `${2 * 1024 * 1024}`, 10); // 2MB

let client;
let pkgVersion = 'unknown';

// Try to get package version
try {
  const fs = await import('fs');
  const { createRequire } = await import('module');
  const require = createRequire(import.meta.url);
  const pkgPath = require.resolve('@github/copilot-sdk/package.json');
  const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
  pkgVersion = pkg.version;
} catch {
  pkgVersion = 'unknown';
}

// Create and start the client
client = new CopilotClient({
  autoStart: true,
  autoRestart: true,
  logLevel: process.env.COPILOT_LOG_LEVEL || 'warning',
});

await client.start();
console.log(`Copilot SDK client started (version ${pkgVersion})`);

// Session cache for quick lookup
const sessions = new Map();

async function handleRpc(method, params = {}) {
  switch (method) {
    case 'ping': {
      const result = await client.ping(params?.message);
      return { ok: true, pong: true, ...result };
    }

    case 'status.get': {
      return { 
        ok: true, 
        version: pkgVersion, 
        state: client.getState()
      };
    }

    case 'auth.status': {
      try {
        await client.ping();
        return { authenticated: true };
      } catch (err) {
        return { authenticated: false, error: err?.message ?? String(err) };
      }
    }

    case 'models.list': {
      return { 
        models: [
          'gpt-5', 'gpt-5.1', 'gpt-5.2', 'gpt-5-mini',
          'gpt-4.1', 'gpt-5.1-codex', 'gpt-5.1-codex-mini', 'gpt-5.1-codex-max', 'gpt-5.2-codex',
          'claude-sonnet-4.5', 'claude-haiku-4.5', 'claude-opus-4.5', 'claude-sonnet-4',
          'gemini-3-pro-preview'
        ]
      };
    }

    case 'session.create': {
      const config = {};
      if (params.sessionId) config.sessionId = params.sessionId;
      if (params.model) config.model = params.model;
      if (params.tools) config.tools = params.tools;
      if (params.systemMessage) config.systemMessage = { content: params.systemMessage };
      if (params.streaming !== undefined) config.streaming = params.streaming;
      if (params.mcpServers) config.mcpServers = params.mcpServers;
      if (params.customAgents) config.customAgents = params.customAgents;
      if (params.skillDirectories) config.skillDirectories = params.skillDirectories;
      if (params.disabledSkills) config.disabledSkills = params.disabledSkills;

      const session = await client.createSession(config);
      sessions.set(session.sessionId, session);

      if (params.prompt) {
        const response = await session.sendAndWait({ 
          prompt: params.prompt, 
          mode: params.mode ?? 'enqueue',
          attachments: params.attachments 
        });
        return { 
          sessionId: session.sessionId, 
          workspacePath: session.workspacePath,
          response: response?.data?.content 
        };
      }

      return { sessionId: session.sessionId, workspacePath: session.workspacePath };
    }

    case 'session.resume': {
      const sessionId = params.sessionId;
      if (!sessionId) throw { code: -32602, message: "'sessionId' is required" };

      const config = {};
      if (params.tools) config.tools = params.tools;
      if (params.streaming !== undefined) config.streaming = params.streaming;
      if (params.mcpServers) config.mcpServers = params.mcpServers;
      if (params.customAgents) config.customAgents = params.customAgents;

      const session = await client.resumeSession(sessionId, config);
      sessions.set(session.sessionId, session);

      if (params.prompt) {
        const response = await session.sendAndWait({ 
          prompt: params.prompt, 
          mode: params.mode ?? 'enqueue',
          attachments: params.attachments 
        });
        return { 
          sessionId: session.sessionId, 
          workspacePath: session.workspacePath,
          response: response?.data?.content 
        };
      }

      return { sessionId: session.sessionId, workspacePath: session.workspacePath };
    }

    case 'session.send': {
      const sessionId = params.sessionId;
      const prompt = params.prompt;
      if (!sessionId) throw { code: -32602, message: "'sessionId' is required" };
      if (!prompt) throw { code: -32602, message: "'prompt' is required" };

      let session = sessions.get(sessionId);
      if (!session) {
        session = await client.resumeSession(sessionId);
        sessions.set(sessionId, session);
      }

      const response = await session.sendAndWait({ 
        prompt, 
        mode: params.mode ?? 'enqueue',
        attachments: params.attachments 
      });

      return { 
        ok: true, 
        response: response?.data?.content,
        messageId: response?.data?.messageId
      };
    }

    case 'session.list': {
      const list = await client.listSessions();
      return { sessions: list };
    }

    case 'session.delete': {
      const sessionId = params.sessionId;
      if (!sessionId) throw { code: -32602, message: "'sessionId' is required" };
      
      const session = sessions.get(sessionId);
      if (session) {
        try { await session.destroy(); } catch { /* ignore */ }
        sessions.delete(sessionId);
      }
      
      await client.deleteSession(sessionId);
      return { ok: true };
    }

    default:
      throw { code: -32601, message: 'Method not found' };
  }
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => {
      body += chunk;
      if (body.length > MAX_BODY) {
        reject(new Error('Payload too large'));
        req.destroy();
      }
    });
    req.on('end', () => {
      try {
        const json = JSON.parse(body || '{}');
        resolve(json);
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

function writeJson(res, status, data) {
  const out = JSON.stringify(data);
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(out);
}

const server = http.createServer(async (req, res) => {
  try {
    if (req.method === 'GET' && req.url?.startsWith('/health')) {
      writeJson(res, 200, { ok: true, version: pkgVersion });
      return;
    }
    if (req.method !== 'POST' || !req.url?.startsWith('/jsonrpc')) {
      writeJson(res, 404, { error: 'not found' });
      return;
    }
    const rpc = await readJson(req);
    const { method, params, id } = rpc;
    if (!method) throw { code: -32600, message: 'Invalid Request' };
    const result = await handleRpc(method, params);
    writeJson(res, 200, { jsonrpc: '2.0', id: id ?? null, result });
  } catch (err) {
    const code = err?.code ?? -32603;
    const message = err?.message ?? String(err);
    try {
      writeJson(res, 200, { jsonrpc: '2.0', id: null, error: { code, message } });
    } catch {
      res.writeHead(500);
      res.end();
    }
  }
});

server.listen(PORT, () => {
  console.log(`Copilot SDK HTTP JSON-RPC server listening on http://localhost:${PORT}/jsonrpc`);
});

// Graceful shutdown
process.on('SIGINT', async () => {
  console.log('Shutting down...');
  try {
    if (client) await client.stop();
  } catch { /* ignore */ }
  server.close(() => process.exit(0));
});

process.on('SIGTERM', async () => {
  console.log('Terminating...');
  try {
    if (client) await client.stop();
  } catch { /* ignore */ }
  server.close(() => process.exit(0));
});
