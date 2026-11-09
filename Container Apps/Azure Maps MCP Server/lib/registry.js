'use strict';

const { publicTool } = require('./define');

const modules = ['search', 'route', 'weather', 'timezone', 'traffic', 'render'].map((name) => require(`./tools/${name}`));
const ALL = modules.flatMap((m) => m.tools);

const byName = new Map();
for (const t of ALL) {
  if (byName.has(t.name)) throw new Error(`Duplicate tool name: ${t.name}`);
  byName.set(t.name, t);
}

const TOOLS = ALL.map(publicTool);

const get = (name) => byName.get(name);

module.exports = { TOOLS, get, count: ALL.length };
