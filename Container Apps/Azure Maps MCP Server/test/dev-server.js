'use strict';

// Local preview of the apps. Serves a small stand-in MCP Apps host and the /mcp endpoint, using a local Azure Maps key
// instead of Microsoft Entra sign-in.
//   $env:MAPS_SUBSCRIPTION_KEY = "<key>"
//   node test/dev-server.js            then open http://127.0.0.1:8777/
//   http://127.0.0.1:8777/?tool=get_weather&args={"location":{"query":"Seattle"}}
const http = require('http');
const fs = require('fs');
const path = require('path');
const { handleMcp } = require('../mcp');
const { createMapsAuth } = require('../maps-auth');

const PORT = Number(process.env.PORT) || 8777;
const deps = {
  authenticate: async () => ({ ok: true, subject: 'local' }),
  maps: createMapsAuth({ mapsClientId: undefined })
};

http.createServer(async (req, res) => {
  const url = (req.url || '/').split('?')[0];
  if (req.method === 'GET' && (url === '/' || url === '/host.html')) {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    return res.end(fs.readFileSync(path.join(__dirname, 'host.html')));
  }
  if (req.method === 'POST' && url === '/mcp') {
    let raw = '';
    for await (const chunk of req) raw += chunk;
    try {
      const message = JSON.parse(raw);
      if (message.method === 'resources/read') require('../lib/widgets').reload();
      const result = await handleMcp(message, 'Bearer local', deps);
      res.writeHead(result.status === 202 ? 202 : 200, { 'Content-Type': 'application/json', ...(result.headers || {}) });
      return res.end(result.status === 202 ? '' : JSON.stringify(result.body));
    } catch (err) {
      res.writeHead(500);
      return res.end(String(err));
    }
  }
  res.writeHead(404);
  res.end('Not found');
}).listen(PORT, '127.0.0.1', () => console.log(`Preview at http://127.0.0.1:${PORT}/`));
