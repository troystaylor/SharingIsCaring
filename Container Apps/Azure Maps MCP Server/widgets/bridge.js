/* MCP Apps bridge: talks to the host over postMessage (JSON-RPC 2.0). Exposes window.App. */
(function () {
  'use strict';

  var nextId = 1;
  var pending = {};
  var listeners = {};
  var hostContext = {};
  var hostCapabilities = null;
  var root = document.documentElement;

  function post(message) { window.parent.postMessage(message, '*'); }
  function notify(method, params) { post({ jsonrpc: '2.0', method: method, params: params || {} }); }

  function request(method, params, timeoutMs) {
    return new Promise(function (resolve, reject) {
      var id = nextId++;
      pending[id] = { resolve: resolve, reject: reject };
      post({ jsonrpc: '2.0', id: id, method: method, params: params || {} });
      setTimeout(function () {
        if (pending[id]) { delete pending[id]; reject(new Error('Timed out waiting for ' + method)); }
      }, timeoutMs || 15000);
    });
  }

  function emit(name, payload) {
    (listeners[name] || []).slice().forEach(function (fn) {
      try { fn(payload); } catch (err) { if (window.console) console.error(err); }
    });
  }

  function applyHostContext(context) {
    if (!context) return;
    for (var k in context) if (Object.prototype.hasOwnProperty.call(context, k)) hostContext[k] = context[k];
    if (context.theme === 'dark' || context.theme === 'light') root.setAttribute('data-theme', context.theme);
    if (context.locale) root.lang = context.locale;
    if (context.displayMode) root.setAttribute('data-display', context.displayMode);
    emit('context', hostContext);
  }

  // Start from the system theme, then follow the host.
  if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) root.setAttribute('data-theme', 'dark');

  function textOf(result) {
    return ((result && result.content) || []).filter(function (p) { return p.type === 'text'; }).map(function (p) { return p.text; }).join('\n');
  }

  window.addEventListener('message', function (event) {
    var m = event.data;
    if (!m || m.jsonrpc !== '2.0') return;

    if (m.id !== undefined && !m.method) {
      var waiting = pending[m.id];
      if (waiting) {
        delete pending[m.id];
        if (m.error) waiting.reject(new Error(m.error.message || 'Request failed')); else waiting.resolve(m.result);
      }
      return;
    }

    switch (m.method) {
      case 'ui/notifications/tool-input': emit('input', (m.params || {}).arguments || {}); break;
      case 'ui/notifications/tool-input-partial': emit('inputPartial', (m.params || {}).arguments || {}); break;
      case 'ui/notifications/tool-result': emit('result', m.params); break;
      case 'ui/notifications/tool-cancelled': emit('cancelled', m.params); break;
      case 'ui/notifications/host-context-changed': applyHostContext(m.params); break;
      case 'ui/resource-teardown':
      case 'ping':
        if (m.id !== undefined) post({ jsonrpc: '2.0', id: m.id, result: {} });
        break;
      default: break;
    }
  });

  var App = {
    textOf: textOf,

    /** The structured data of a tool result, or null. */
    dataOf: function (result) { return (result && result.structuredContent) || null; },

    on: function (name, fn) { (listeners[name] = listeners[name] || []).push(fn); },

    get hostContext() { return hostContext; },

    /** Starts the conversation with the host. Resolves once the host has answered. */
    init: function (info) {
      return request('ui/initialize', {
        protocolVersion: '2026-01-26',
        appInfo: { name: 'azure-maps-' + info.name, version: '1.0.0' },
        appCapabilities: { availableDisplayModes: ['inline', 'fullscreen'] }
      }, 8000).then(function (result) {
        hostCapabilities = (result && result.hostCapabilities) || null;
        applyHostContext(result && result.hostContext);
        notify('ui/notifications/initialized');
        App.reportSize();
        return result;
      });
    },

    /** Calls one of this server's tools through the host, which adds the user's sign-in. */
    callTool: function (name, args, timeoutMs) {
      return request('tools/call', { name: name, arguments: args || {} }, timeoutMs || 45000).then(function (result) {
        if (result && result.isError) throw new Error(textOf(result) || 'The tool failed.');
        return result;
      });
    },

    openLink: function (url) { return request('ui/open-link', { url: url }).catch(function () {}); },

    canDownload: function () { return !hostCapabilities || !!hostCapabilities.downloadFile; },

    download: function (filename, mimeType, text) {
      return request('ui/download-file', {
        contents: [{ type: 'resource', resource: { uri: 'file:///' + filename, mimeType: mimeType, text: text } }]
      }, 20000);
    },

    /** Posts a message to the chat as if the user wrote it. */
    sendMessage: function (text) {
      return request('ui/message', { role: 'user', content: [{ type: 'text', text: text }] }).catch(function () {});
    },

    /** Tells the agent what the user is looking at or has chosen, so its next answer can use it. */
    updateModelContext: function (text, structured) {
      var params = { content: [{ type: 'text', text: text }] };
      if (structured) params.structuredContent = structured;
      return request('ui/update-model-context', params).catch(function () {});
    },

    canFullscreen: function () {
      var modes = hostContext.availableDisplayModes;
      return !modes || modes.indexOf('fullscreen') >= 0;
    },

    toggleFullscreen: function () {
      var next = hostContext.displayMode === 'fullscreen' ? 'inline' : 'fullscreen';
      return request('ui/request-display-mode', { mode: next }).then(function (r) {
        if (r && r.mode) applyHostContext({ displayMode: r.mode });
      }).catch(function () {});
    },

    reportSize: function () {
      var h = Math.ceil(document.documentElement.scrollHeight);
      var w = Math.ceil(document.documentElement.scrollWidth);
      notify('ui/notifications/size-changed', { width: w, height: h });
    }
  };

  if (typeof ResizeObserver !== 'undefined') {
    new ResizeObserver(function () { App.reportSize(); }).observe(document.body);
  }

  window.App = App;
})();
