'use strict';

const http = require('http');
const { handleMcp } = require('./mcp');
const { createEntraValidator } = require('./auth');
const { createMapsAuth } = require('./maps-auth');

const PORT = process.env.PORT || 8080;
const MAX_BODY_BYTES = 1024 * 1024;

const { TENANT_ID, CLIENT_ID, MAPS_CLIENT_ID } = process.env;
if (!TENANT_ID || !CLIENT_ID) {
  console.error('TENANT_ID and CLIENT_ID are required: the Microsoft Entra tenant and the app registration that protects this server.');
  process.exit(1);
}

const deps = {
  authenticate: createEntraValidator({ tenantId: TENANT_ID, clientId: CLIENT_ID }).validate,
  maps: createMapsAuth({ mapsClientId: MAPS_CLIENT_ID })
};

function send(res, status, body, headers = {}) {
  const payload = body === undefined ? '' : JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json', ...headers });
  res.end(payload);
}

const server = http.createServer(async (req, res) => {
  const path = (req.url || '').split('?')[0];

  if (path === '/health') return send(res, 200, { status: 'ok' });
  if (path !== '/mcp') return send(res, 404, { error: 'Not found' });
  if (req.method !== 'POST') return send(res, 405, { error: 'This MCP server accepts POST only.' }, { Allow: 'POST' });

  let raw = '';
  for await (const chunk of req) {
    raw += chunk;
    if (raw.length > MAX_BODY_BYTES) return send(res, 413, { error: 'Request too large' });
  }

  let message;
  try {
    message = JSON.parse(raw);
  } catch {
    return send(res, 400, { jsonrpc: '2.0', error: { code: -32700, message: 'Parse error' }, id: null });
  }

  try {
    const result = await handleMcp(message, req.headers.authorization, deps);
    if (result.status === 202) {
      res.writeHead(202);
      return res.end();
    }
    return send(res, result.status, result.body, result.headers);
  } catch (err) {
    console.error('Unhandled error', err);
    return send(res, 500, { jsonrpc: '2.0', error: { code: -32603, message: 'Internal error' }, id: message.id ?? null });
  }
});

server.listen(PORT, () => console.log(`azure-maps-route-mcp listening on ${PORT}`));
