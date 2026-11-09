'use strict';

const MAX_TEXT_CHARS = 60000;

/**
 * Defines one MCP tool. Every tool in this server only reads data, so the annotations say so. A tool can declare an
 * MCP Apps widget (widget: 'map') or be hidden from the agent and callable only by widgets (appOnly: true).
 */
function defineTool({ name, title, description, properties = {}, required = [], widget, appOnly = false, handler }) {
  const inputSchema = { type: 'object', properties };
  if (required.length) inputSchema.required = required;

  const ui = {};
  if (widget) ui.resourceUri = `ui://azure-maps/${widget}.html`;
  if (appOnly) ui.visibility = ['app'];

  return {
    name,
    title,
    description,
    inputSchema,
    annotations: { title, readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: true },
    ...(Object.keys(ui).length ? { _meta: { ui, ...(ui.resourceUri ? { 'ui/resourceUri': ui.resourceUri } : {}) } } : {}),
    widget,
    handler
  };
}

/** The part of a tool definition that goes in tools/list. */
const publicTool = ({ handler, widget, ...rest }) => rest;

/** A successful result. structured is what widgets read; text is what the agent reads. */
function ok(text, structured, extraContent = []) {
  return {
    content: [{ type: 'text', text }, ...extraContent],
    ...(structured === undefined ? {} : { structuredContent: structured }),
    isError: false
  };
}

/**
 * Serializes a value for the agent. Large results can overflow a client's limit, so long lists are shortened step by
 * step until the text fits, and the caller is told what was cut.
 */
function jsonText(value, maxChars = MAX_TEXT_CHARS) {
  let text = JSON.stringify(value);
  if (text.length <= maxChars) return { text, truncated: false };

  const shorten = (v, cap) => {
    if (Array.isArray(v)) return v.slice(0, cap).map((x) => shorten(x, cap));
    if (v && typeof v === 'object') {
      const out = {};
      for (const [k, x] of Object.entries(v)) out[k] = shorten(x, cap);
      return out;
    }
    return v;
  };
  for (const cap of [200, 100, 50, 25, 10, 5, 2]) {
    text = JSON.stringify(shorten(value, cap));
    if (text.length <= maxChars) return { text, truncated: true };
  }
  return { text: text.slice(0, maxChars), truncated: true };
}

/** Text result whose body is the JSON of data, with a one-line lead for the agent. */
function dataResult(lead, data, structured) {
  const { text, truncated } = jsonText(data);
  const note = truncated ? ' Long lists in this result were shortened to fit.' : '';
  return ok(`${lead}${note}\n${text}`, structured);
}

module.exports = { defineTool, publicTool, ok, jsonText, dataResult, MAX_TEXT_CHARS };
