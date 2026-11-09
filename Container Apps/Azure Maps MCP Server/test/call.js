'use strict';

// Calls one tool directly with a local Azure Maps key and prints a short summary of the result.
//   $env:MAPS_SUBSCRIPTION_KEY = "<key>"
//   node test/call.js get_weather '{"location":{"query":"Seattle"}}'
const { callTool } = require('../mcp');
const { createMapsAuth } = require('../maps-auth');

(async () => {
  const [name, json = '{}', mode = 'summary'] = process.argv.slice(2);
  const started = Date.now();
  try {
    const result = await callTool(name, JSON.parse(json), { maps: createMapsAuth({ mapsClientId: undefined }) });
    if (!result) { console.log('Unknown tool'); process.exitCode = 1; return; }
    const text = result.content.filter((c) => c.type === 'text').map((c) => c.text).join('\n');
    const images = result.content.filter((c) => c.type === 'image');
    console.log(`${result.isError ? 'ERROR' : 'OK'} ${name} in ${Date.now() - started} ms`);
    console.log(mode === 'full' ? text : text.slice(0, 1800) + (text.length > 1800 ? `\n...[${text.length} chars]` : ''));
    if (images.length) console.log(`images: ${images.map((i) => `${i.mimeType} ${Math.round(Buffer.from(i.data, 'base64').length / 1024)} KB`).join(', ')}`);
    if (result.structuredContent) {
      const s = JSON.stringify(result.structuredContent);
      console.log(`structuredContent: kind=${result.structuredContent.kind} size=${(s.length / 1024).toFixed(1)} KB`);
      if (mode === 'structured') console.log(s);
    }
  } catch (err) {
    console.log(`THROWN ${name}: ${err.name}: ${err.message}`);
    process.exitCode = 1;
  }
})();
