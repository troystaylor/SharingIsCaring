/* A small interactive web map for the apps. Tiles come from the server's get_map_tile tool through the host, so the
   page needs no network access. Overlays (shapes, pins) are drawn on a canvas. Exposes window.MapView. */
(function () {
  'use strict';

  var TILE = 256;
  var MIN_ZOOM = 1;
  var MAX_ZOOM = 18;
  var MAX_IN_FLIGHT = 10;
  var MAX_CACHED = 500;
  var STYLES = {
    road: ['microsoft.base.road'],
    dark: ['microsoft.base.darkgrey'],
    satellite: ['microsoft.imagery'],
    hybrid: ['microsoft.imagery', 'microsoft.base.hybrid.road'],
    terrain: ['microsoft.terra.main']
  };
  var OVERLAYS = {
    traffic: { tilesetId: 'microsoft.traffic.relative.main', opacity: 1 },
    radar: { tilesetId: 'microsoft.weather.radar.main', opacity: 0.75 },
    infrared: { tilesetId: 'microsoft.weather.infrared.main', opacity: 0.7 }
  };

  var rad = function (d) { return d * Math.PI / 180; };
  var clamp = function (v, lo, hi) { return Math.max(lo, Math.min(hi, v)); };

  function worldSize(zoom) { return TILE * Math.pow(2, zoom); }
  function lonToX(lon, zoom) { return (lon + 180) / 360 * worldSize(zoom); }
  function latToY(lat, zoom) {
    var s = Math.sin(rad(clamp(lat, -85.0511, 85.0511)));
    return (0.5 - Math.log((1 + s) / (1 - s)) / (4 * Math.PI)) * worldSize(zoom);
  }
  function xToLon(x, zoom) { return x / worldSize(zoom) * 360 - 180; }
  function yToLat(y, zoom) {
    var n = Math.PI - 2 * Math.PI * y / worldSize(zoom);
    return 180 / Math.PI * Math.atan(0.5 * (Math.exp(n) - Math.exp(-n)));
  }

  function MapView(container, options) {
    var self = this;
    this.options = options || {};
    this.container = container;
    this.container.classList.add('map');
    this.container.setAttribute('tabindex', '0');
    this.container.setAttribute('role', 'application');
    this.container.setAttribute('aria-label', this.options.label || 'Map. Use the arrow keys to pan and plus and minus to zoom.');

    this.canvas = document.createElement('canvas');
    this.container.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d');

    this.view = { lon: 0, lat: 20, zoom: 2 };
    this.size = { w: 300, h: 300 };
    this.styleName = this.options.style || 'road';
    this.overlays = {};
    this.layers = [];
    this.markers = [];
    this.selected = null;
    this.handlers = {};
    this.tiles = new Map();
    this.queue = [];
    this.inFlight = 0;
    this.failures = 0;
    this.drawPending = false;
    this.pointers = {};
    this.gesture = null;
    this.interactive = this.options.interactive !== false;
    this.fetchTile = this.options.fetchTile || function (args) { return App.callTool('get_map_tile', args, 30000); };

    this.attribution = el('div', { class: 'attribution', text: '© TomTom, Microsoft' });
    this.container.appendChild(this.attribution);
    this.hint = el('div', { class: 'hint', text: 'Use Ctrl + scroll to zoom' });
    this.container.appendChild(this.hint);
    this.spinner = el('div', { class: 'loading hidden', role: 'progressbar', 'aria-label': 'Loading map' });
    this.container.appendChild(this.spinner);
    this.notice = el('div', { class: 'overlay-bl hidden' });
    this.container.appendChild(this.notice);
    this.popup = el('div', { class: 'popup hidden', role: 'dialog' });
    this.container.appendChild(this.popup);

    if (this.interactive) this.buildControls();
    this.bindEvents();

    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(function () { self.resize(); }).observe(container);
    this.resize();
  }

  function el(tag, props) {
    var n = document.createElement(tag);
    for (var k in props) { if (k === 'text') n.textContent = props[k]; else n.setAttribute(k, props[k]); }
    return n;
  }

  var proto = MapView.prototype;

  proto.on = function (name, fn) { (this.handlers[name] = this.handlers[name] || []).push(fn); return this; };
  proto.emit = function (name, payload) { (this.handlers[name] || []).forEach(function (fn) { fn(payload); }); };

  proto.buildControls = function () {
    var self = this;
    var box = el('div', { class: 'controls' });
    function button(label, iconName, fn) {
      var b = el('button', { class: 'btn', type: 'button', 'aria-label': label, title: label });
      b.innerHTML = UI.icon(iconName);
      b.addEventListener('click', function (e) { e.stopPropagation(); fn(); });
      b.addEventListener('pointerdown', function (e) { e.stopPropagation(); });
      box.appendChild(b);
      return b;
    }
    button('Zoom in', 'plus', function () { self.zoomBy(1); });
    button('Zoom out', 'minus', function () { self.zoomBy(-1); });
    if (this.options.fullscreen !== false && window.App) {
      this.fullscreenButton = button('Full screen', 'expand', function () { App.toggleFullscreen(); });
      App.on('context', function (ctx) {
        self.fullscreenButton.classList.toggle('hidden', !App.canFullscreen());
        var full = ctx.displayMode === 'fullscreen';
        self.fullscreenButton.innerHTML = UI.icon(full ? 'collapse' : 'expand');
        self.fullscreenButton.setAttribute('aria-label', full ? 'Exit full screen' : 'Full screen');
        self.fullscreenButton.title = full ? 'Exit full screen' : 'Full screen';
        self.resize();
      });
    }
    this.container.appendChild(box);
  };

  // ───────────── view ─────────────

  proto.resize = function () {
    var rect = this.container.getBoundingClientRect();
    var w = Math.max(50, Math.round(rect.width));
    var h = Math.max(50, Math.round(rect.height));
    var dpr = Math.min(2, window.devicePixelRatio || 1);
    this.size = { w: w, h: h };
    this.dpr = dpr;
    this.canvas.width = Math.round(w * dpr);
    this.canvas.height = Math.round(h * dpr);
    this.schedule();
  };

  proto.setView = function (lon, lat, zoom) {
    this.view = { lon: clamp(lon, -180, 180), lat: clamp(lat, -84, 84), zoom: clamp(zoom, MIN_ZOOM, MAX_ZOOM) };
    this.schedule();
    this.emit('move', this.view);
  };

  proto.zoomBy = function (delta, anchor) {
    var v = this.view;
    var next = clamp(v.zoom + delta, MIN_ZOOM, MAX_ZOOM);
    if (next === v.zoom) return;
    if (anchor) {
      // Keep the point under the anchor fixed while zooming.
      var cx = lonToX(v.lon, v.zoom), cy = latToY(v.lat, v.zoom);
      var wx = cx + (anchor.x - this.size.w / 2), wy = cy + (anchor.y - this.size.h / 2);
      var lon = xToLon(wx, v.zoom), lat = yToLat(wy, v.zoom);
      var nx = lonToX(lon, next) - (anchor.x - this.size.w / 2), ny = latToY(lat, next) - (anchor.y - this.size.h / 2);
      this.setView(xToLon(nx, next), yToLat(ny, next), next);
    } else {
      this.setView(v.lon, v.lat, next);
    }
    this.emit('moveend', this.view);
  };

  /** Fits [minLon, minLat, maxLon, maxLat] in the view. */
  proto.fitBounds = function (bbox, padding) {
    if (!bbox) return;
    var pad = padding === undefined ? 32 : padding;
    var west = bbox[0], south = bbox[1], east = bbox[2], north = bbox[3];
    var w = Math.max(50, this.size.w - pad * 2), h = Math.max(50, this.size.h - pad * 2);
    var zoom = MAX_ZOOM - 3;
    for (var z = MAX_ZOOM - 3; z >= MIN_ZOOM; z--) {
      var dx = lonToX(east, z) - lonToX(west, z);
      var dy = latToY(south, z) - latToY(north, z);
      if (dx <= w && dy <= h) { zoom = z; break; }
      zoom = z;
    }
    var cx = (lonToX(west, zoom) + lonToX(east, zoom)) / 2;
    var cy = (latToY(north, zoom) + latToY(south, zoom)) / 2;
    this.setView(xToLon(cx, zoom), yToLat(cy, zoom), zoom);
    this.emit('moveend', this.view);
  };

  proto.getBounds = function () {
    var v = this.view;
    var cx = lonToX(v.lon, v.zoom), cy = latToY(v.lat, v.zoom);
    return [
      xToLon(cx - this.size.w / 2, v.zoom), yToLat(cy + this.size.h / 2, v.zoom),
      xToLon(cx + this.size.w / 2, v.zoom), yToLat(cy - this.size.h / 2, v.zoom)
    ];
  };

  /** Longitude and latitude to pixels in the view. */
  proto.project = function (lon, lat) {
    var v = this.view;
    return {
      x: lonToX(lon, v.zoom) - lonToX(v.lon, v.zoom) + this.size.w / 2,
      y: latToY(lat, v.zoom) - latToY(v.lat, v.zoom) + this.size.h / 2
    };
  };

  proto.unproject = function (x, y) {
    var v = this.view;
    var wx = lonToX(v.lon, v.zoom) + (x - this.size.w / 2);
    var wy = latToY(v.lat, v.zoom) + (y - this.size.h / 2);
    return { lon: xToLon(wx, v.zoom), lat: yToLat(wy, v.zoom) };
  };

  // ───────────── content ─────────────

  proto.setStyle = function (name) {
    if (!STYLES[name]) return;
    this.styleName = name;
    this.schedule();
    this.emit('style', name);
  };

  /** spec: a name from OVERLAYS, or { tilesetId, opacity, frames: [iso, ...], frameIndex }. Pass null to remove. */
  proto.setOverlay = function (name, spec) {
    if (!spec) delete this.overlays[name];
    else this.overlays[name] = Object.assign({}, OVERLAYS[name] || {}, spec === true ? {} : spec);
    this.schedule();
  };

  proto.setLayers = function (layers) { this.layers = layers || []; this.schedule(); };
  proto.setMarkers = function (markers) {
    this.markers = markers || [];
    if (this.selected && !this.markers.some(function (m) { return m.id === this.selected; }, this)) this.selected = null;
    this.schedule();
  };

  proto.select = function (id, scroll) {
    this.selected = id;
    var m = this.markerById(id);
    if (m && scroll) {
      var p = this.project(m.longitude, m.latitude);
      if (p.x < 24 || p.y < 40 || p.x > this.size.w - 24 || p.y > this.size.h - 24) this.setView(m.longitude, m.latitude, this.view.zoom);
    }
    this.schedule();
  };

  proto.markerById = function (id) { for (var i = 0; i < this.markers.length; i++) if (this.markers[i].id === id) return this.markers[i]; return null; };

  proto.bboxOfContent = function () {
    var pts = [];
    this.markers.forEach(function (m) { pts.push([m.longitude, m.latitude]); });
    function add(c) { if (typeof c[0] === 'number') pts.push([c[0], c[1]]); else c.forEach(add); }
    this.layers.forEach(function (l) {
      if (l.type === 'line') add(l.coordinates);
      else if (l.type === 'polygon') add(l.geometry.coordinates);
      else if (l.type === 'circle') {
        var dLat = l.radiusMeters / 111320, dLon = l.radiusMeters / (111320 * Math.max(0.01, Math.cos(rad(l.latitude))));
        pts.push([l.longitude - dLon, l.latitude - dLat], [l.longitude + dLon, l.latitude + dLat]);
      }
    });
    if (!pts.length) return null;
    var b = [180, 90, -180, -90];
    pts.forEach(function (p) { b[0] = Math.min(b[0], p[0]); b[1] = Math.min(b[1], p[1]); b[2] = Math.max(b[2], p[0]); b[3] = Math.max(b[3], p[1]); });
    return b;
  };

  proto.fitContent = function (padding) {
    var b = this.bboxOfContent();
    if (!b) return;
    if (b[0] === b[2] && b[1] === b[3]) this.setView(b[0], b[1], 14);
    else this.fitBounds(b, padding);
  };

  proto.showNotice = function (node) {
    this.notice.textContent = '';
    if (node) { this.notice.appendChild(node); this.notice.classList.remove('hidden'); } else this.notice.classList.add('hidden');
  };

  // ───────────── tiles ─────────────

  function tileKey(tilesetId, z, x, y, stamp) { return tilesetId + '|' + z + '|' + x + '|' + y + '|' + (stamp || ''); }

  proto.tileFor = function (tilesetId, z, x, y, stamp, fetchIfMissing) {
    var key = tileKey(tilesetId, z, x, y, stamp);
    var t = this.tiles.get(key);
    if (t) { t.used = Date.now(); return t; }
    if (!fetchIfMissing) return null;
    t = { key: key, state: 'queued', tilesetId: tilesetId, z: z, x: x, y: y, stamp: stamp, used: Date.now(), attempts: 0 };
    this.tiles.set(key, t);
    this.queue.push(t);
    this.pump();
    return t;
  };

  proto.pump = function () {
    var self = this;
    while (this.inFlight < MAX_IN_FLIGHT && this.queue.length) {
      // Newest requests first, so what is on screen now loads before what was left behind.
      var t = this.queue.pop();
      if (t.state !== 'queued') continue;
      if (!this.wanted(t)) { this.tiles.delete(t.key); continue; }
      this.load(t);
    }
    this.spinner.classList.toggle('hidden', !(this.inFlight || this.queue.length));
    if (this.options.onLoading) this.options.onLoading(this.inFlight + this.queue.length);
  };

  proto.wanted = function (t) {
    if (t.prefetch) return true;
    var v = this.view;
    var zi = Math.round(v.zoom);
    if (Math.abs(t.z - zi) > 1) return false;
    var scale = Math.pow(2, v.zoom - t.z);
    var cx = lonToX(v.lon, v.zoom) / scale, cy = latToY(v.lat, v.zoom) / scale;
    var halfW = this.size.w / 2 / scale + TILE, halfH = this.size.h / 2 / scale + TILE;
    var n = Math.pow(2, t.z);
    var tx = t.x * TILE + TILE / 2, ty = t.y * TILE + TILE / 2;
    var dx = Math.abs(tx - cx); dx = Math.min(dx, n * TILE - dx);
    return dx <= halfW && Math.abs(ty - cy) <= halfH;
  };

  proto.load = function (t) {
    var self = this;
    t.state = 'loading';
    this.inFlight++;
    var args = { tilesetId: t.tilesetId, zoom: t.z, x: t.x, y: t.y };
    if (t.stamp) args.timeStamp = t.stamp;
    if (this.options.language) args.language = this.options.language;
    Promise.resolve(this.fetchTile(args)).then(function (result) {
      var image = ((result && result.content) || []).filter(function (p) { return p.type === 'image'; })[0];
      if (!image) throw new Error('no image');
      return new Promise(function (resolve, reject) {
        var img = new Image();
        img.onload = function () { resolve(img); };
        img.onerror = function () { reject(new Error('bad image')); };
        img.src = 'data:' + image.mimeType + ';base64,' + image.data;
      });
    }).then(function (img) {
      t.img = img; t.state = 'ready'; self.failures = 0;
      self.showNotice(null);
      self.schedule();
    }).catch(function () {
      t.attempts++;
      self.failures++;
      if (t.attempts < 2) { t.state = 'queued'; self.queue.unshift(t); } else { t.state = 'error'; }
      if (self.failures >= 6 && !self.noticeShown) {
        self.noticeShown = true;
        self.showNotice(el('div', { class: 'message warning', text: 'Map tiles could not be loaded. The shapes and pins are still shown.' }));
        self.emit('tileerror');
      }
    }).then(function () {
      self.inFlight--;
      self.evict();
      self.pump();
    });
  };

  proto.evict = function () {
    if (this.tiles.size <= MAX_CACHED) return;
    var entries = Array.from(this.tiles.values()).filter(function (t) { return t.state === 'ready' || t.state === 'error'; }).sort(function (a, b) { return a.used - b.used; });
    var drop = this.tiles.size - MAX_CACHED;
    for (var i = 0; i < drop && i < entries.length; i++) this.tiles.delete(entries[i].key);
  };

  /** Starts loading every frame of an overlay for the current view, so playing it back doesn't stall. */
  proto.prefetchOverlay = function (name) {
    var o = this.overlays[name];
    if (!o || !o.frames) return;
    var self = this;
    o.frames.forEach(function (stamp) { self.drawTileSet(null, o.tilesetId, stamp, true); });
  };

  proto.frameProgress = function (name) {
    var o = this.overlays[name];
    if (!o || !o.frames) return 1;
    var self = this;
    var zi = Math.round(this.view.zoom);
    var tiles = this.visibleTiles(zi);
    if (!tiles.length) return 1;
    var ready = 0, total = 0;
    o.frames.forEach(function (stamp) {
      tiles.forEach(function (t) {
        total++;
        var rec = self.tileFor(o.tilesetId, zi, t.x, t.y, stamp, false);
        if (rec && (rec.state === 'ready' || rec.state === 'error')) ready++;
      });
    });
    return total ? ready / total : 1;
  };

  proto.visibleTiles = function (zi) {
    var v = this.view;
    var scale = Math.pow(2, v.zoom - zi);
    var cx = lonToX(v.lon, v.zoom) / scale, cy = latToY(v.lat, v.zoom) / scale;
    var x0 = Math.floor((cx - this.size.w / 2 / scale) / TILE), x1 = Math.floor((cx + this.size.w / 2 / scale) / TILE);
    var y0 = Math.floor((cy - this.size.h / 2 / scale) / TILE), y1 = Math.floor((cy + this.size.h / 2 / scale) / TILE);
    var n = Math.pow(2, zi);
    var out = [];
    for (var ty = Math.max(0, y0); ty <= Math.min(n - 1, y1); ty++) {
      for (var tx = x0; tx <= x1; tx++) out.push({ x: ((tx % n) + n) % n, rawX: tx, y: ty, cx: cx, cy: cy, scale: scale });
    }
    return out;
  };

  proto.drawTileSet = function (ctx, tilesetId, stamp, prefetchOnly) {
    var v = this.view;
    var zi = clamp(Math.round(v.zoom), MIN_ZOOM, MAX_ZOOM);
    var tiles = this.visibleTiles(zi);
    var self = this;
    tiles.forEach(function (t) {
      var rec = self.tileFor(tilesetId, zi, t.x, t.y, stamp, true);
      if (prefetchOnly) { if (rec && rec.state === 'queued') rec.prefetch = true; return; }
      var size = TILE * t.scale;
      var dx = t.rawX * size - t.cx * t.scale + self.size.w / 2;
      var dy = t.y * size - t.cy * t.scale + self.size.h / 2;
      if (rec && rec.state === 'ready') {
        ctx.drawImage(rec.img, Math.floor(dx), Math.floor(dy), Math.ceil(size) + 1, Math.ceil(size) + 1);
      } else {
        // Show the parent tile, enlarged, while this one loads.
        var parent = self.tileFor(tilesetId, zi - 1, t.x >> 1, t.y >> 1, stamp, false);
        if (zi > MIN_ZOOM && parent && parent.state === 'ready') {
          var sx = (t.x & 1) * TILE / 2, sy = (t.y & 1) * TILE / 2;
          ctx.drawImage(parent.img, sx, sy, TILE / 2, TILE / 2, Math.floor(dx), Math.floor(dy), Math.ceil(size) + 1, Math.ceil(size) + 1);
        }
      }
    });
  };

  // ───────────── drawing ─────────────

  proto.schedule = function () {
    var self = this;
    if (this.drawPending) return;
    this.drawPending = true;
    requestAnimationFrame(function () { self.drawPending = false; self.draw(); });
  };

  function css(name, fallback) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback; }

  proto.draw = function () {
    var ctx = this.ctx, w = this.size.w, h = this.size.h;
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    ctx.fillStyle = css('--map-bg', '#e8ecef');
    ctx.fillRect(0, 0, w, h);
    this.drawGrid(ctx);

    var self = this;
    (STYLES[this.styleName] || STYLES.road).forEach(function (id) { self.drawTileSet(ctx, id); });
    Object.keys(this.overlays).forEach(function (name) {
      var o = self.overlays[name];
      ctx.globalAlpha = o.opacity === undefined ? 1 : o.opacity;
      var stamp = o.frames ? o.frames[o.frameIndex || 0] : o.timeStamp;
      self.drawTileSet(ctx, o.tilesetId, stamp);
      ctx.globalAlpha = 1;
    });
    this.pump();

    this.layers.forEach(function (l) { self.drawLayer(ctx, l); });
    this.drawMarkers(ctx);
    this.positionPopup();
  };

  proto.drawGrid = function (ctx) {
    var step = 64;
    ctx.strokeStyle = css('--map-grid', '#d7dde2');
    ctx.lineWidth = 1;
    ctx.beginPath();
    var ox = -(this.view.lon * 3) % step, oy = (this.view.lat * 3) % step;
    for (var x = ox; x < this.size.w; x += step) { ctx.moveTo(x, 0); ctx.lineTo(x, this.size.h); }
    for (var y = oy; y < this.size.h; y += step) { ctx.moveTo(0, y); ctx.lineTo(this.size.w, y); }
    ctx.stroke();
  };

  proto.tracePath = function (ctx, coords) {
    for (var i = 0; i < coords.length; i++) {
      var p = this.project(coords[i][0], coords[i][1]);
      if (i === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y);
    }
  };

  proto.drawLayer = function (ctx, l) {
    var self = this;
    if (l.hidden) return;
    ctx.save();
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    if (l.type === 'polygon') {
      var polys = l.geometry.type === 'Polygon' ? [l.geometry.coordinates] : l.geometry.coordinates;
      polys.forEach(function (rings) {
        ctx.beginPath();
        rings.forEach(function (ring) { self.tracePath(ctx, ring); ctx.closePath(); });
        ctx.fillStyle = l.color;
        ctx.globalAlpha = l.fillOpacity === undefined ? 0.2 : l.fillOpacity;
        ctx.fill('evenodd');
        ctx.globalAlpha = 1;
        ctx.strokeStyle = l.color;
        ctx.lineWidth = l.strokeWidth || 2;
        ctx.stroke();
      });
    } else if (l.type === 'line') {
      var dark = this.styleName === 'dark' || this.styleName === 'satellite' || this.styleName === 'hybrid';
      ctx.beginPath();
      this.tracePath(ctx, l.coordinates);
      if (!l.noCasing) {
        ctx.strokeStyle = dark ? 'rgba(0,0,0,0.55)' : 'rgba(255,255,255,0.9)';
        ctx.lineWidth = (l.width || 4) + 3;
        ctx.stroke();
      }
      ctx.strokeStyle = l.color;
      ctx.lineWidth = l.width || 4;
      ctx.globalAlpha = l.opacity === undefined ? 1 : l.opacity;
      if (l.dashed) ctx.setLineDash([7, 6]);
      ctx.stroke();
    } else if (l.type === 'circle') {
      var c = this.project(l.longitude, l.latitude);
      var mpp = 40075016.686 / worldSize(this.view.zoom) * Math.cos(rad(l.latitude));
      var r = l.radiusMeters / mpp;
      ctx.beginPath();
      ctx.arc(c.x, c.y, Math.max(1, r), 0, Math.PI * 2);
      ctx.fillStyle = l.color;
      ctx.globalAlpha = l.fillOpacity === undefined ? 0.15 : l.fillOpacity;
      ctx.fill();
      ctx.globalAlpha = 1;
      ctx.strokeStyle = l.color;
      ctx.lineWidth = 2;
      ctx.stroke();
    } else if (l.type === 'arrowLine') {
      this.drawLayer(ctx, Object.assign({}, l, { type: 'line' }));
    }
    ctx.restore();
  };

  proto.drawMarkers = function (ctx) {
    var self = this;
    var items = this.markers.map(function (m) { var p = self.project(m.longitude, m.latitude); return { m: m, p: p }; })
      .filter(function (it) { return it.p.x > -30 && it.p.y > -40 && it.p.x < self.size.w + 30 && it.p.y < self.size.h + 30; });
    // Selected pin last so it sits on top.
    items.sort(function (a, b) { return (a.m.id === self.selected) - (b.m.id === self.selected) || a.p.y - b.p.y; });
    items.forEach(function (it) { self.drawMarker(ctx, it.m, it.p, it.m.id === self.selected); });
  };

  proto.drawMarker = function (ctx, m, p, selected) {
    var color = m.color || '#0f6cbd';
    ctx.save();
    ctx.translate(p.x, p.y);
    if (m.small) {
      ctx.beginPath();
      ctx.arc(0, 0, selected ? 7 : 5, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.fill();
      ctx.lineWidth = 2;
      ctx.strokeStyle = '#ffffff';
      ctx.stroke();
      ctx.restore();
      return;
    }
    var s = selected ? 1.2 : 1;
    ctx.scale(s, s);
    ctx.beginPath();
    ctx.moveTo(0, 0);
    ctx.bezierCurveTo(-4, -8, -11, -12, -11, -19);
    ctx.arc(0, -19, 11, Math.PI, 0, false);
    ctx.bezierCurveTo(11, -12, 4, -8, 0, 0);
    ctx.closePath();
    ctx.shadowColor = 'rgba(0,0,0,0.35)';
    ctx.shadowBlur = 4;
    ctx.shadowOffsetY = 1;
    ctx.fillStyle = color;
    ctx.fill();
    ctx.shadowColor = 'transparent';
    ctx.lineWidth = selected ? 2.5 : 1.5;
    ctx.strokeStyle = selected ? (css('--focus-outer', '#000')) : '#ffffff';
    ctx.stroke();
    if (m.label) {
      ctx.fillStyle = '#ffffff';
      ctx.font = '600 11px ' + css('--font-family', 'sans-serif');
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(m.label).slice(0, 3), 0, -19);
    }
    ctx.restore();
  };

  proto.hitMarker = function (x, y) {
    var best = null, bestD = Infinity;
    for (var i = 0; i < this.markers.length; i++) {
      var m = this.markers[i];
      var p = this.project(m.longitude, m.latitude);
      var cy = m.small ? p.y : p.y - 18;
      var r = m.small ? 9 : 14;
      var d = Math.hypot(p.x - x, cy - y);
      if (d <= r && d < bestD) { best = m; bestD = d; }
    }
    return best;
  };

  // ───────────── popup ─────────────

  /** content: a DOM node or null. Shows a card above the marker with that id. */
  proto.setPopup = function (markerId, content) {
    this.popupFor = markerId;
    this.popup.textContent = '';
    if (content) this.popup.appendChild(content);
    this.positionPopup();
  };

  proto.positionPopup = function () {
    var m = this.popupFor !== undefined && this.popupFor !== null ? this.markerById(this.popupFor) : null;
    if (!m || !this.popup.firstChild) { this.popup.classList.add('hidden'); return; }
    var p = this.project(m.longitude, m.latitude);
    var visible = p.x > 0 && p.x < this.size.w && p.y > 30 && p.y < this.size.h;
    this.popup.classList.toggle('hidden', !visible);
    if (!visible) return;
    var halfW = Math.min(130, this.popup.offsetWidth / 2 || 100);
    this.popup.style.left = clamp(p.x, halfW + 4, this.size.w - halfW - 4) + 'px';
    this.popup.style.top = p.y + 'px';
  };

  // ───────────── input ─────────────

  proto.bindEvents = function () {
    var self = this;
    var c = this.container;

    c.addEventListener('pointerdown', function (e) {
      if (!self.interactive || e.button > 0) return;
      c.setPointerCapture(e.pointerId);
      self.pointers[e.pointerId] = { x: e.offsetX, y: e.offsetY, sx: e.offsetX, sy: e.offsetY };
      self.startGesture();
    });
    c.addEventListener('pointermove', function (e) {
      var p = self.pointers[e.pointerId];
      if (!p) {
        if (self.interactive) c.style.cursor = self.hitMarker(e.offsetX, e.offsetY) ? 'pointer' : '';
        return;
      }
      p.x = e.offsetX; p.y = e.offsetY;
      self.updateGesture();
    });
    function end(e) {
      var p = self.pointers[e.pointerId];
      if (!p) return;
      var moved = Math.hypot(p.x - p.sx, p.y - p.sy);
      delete self.pointers[e.pointerId];
      if (Object.keys(self.pointers).length === 0) {
        if (moved < 5 && !(self.gesture && self.gesture.pinched)) self.handleClick(p.x, p.y);
        else self.emit('moveend', self.view);
        self.gesture = null;
      } else {
        self.startGesture();
      }
    }
    c.addEventListener('pointerup', end);
    c.addEventListener('pointercancel', end);

    c.addEventListener('wheel', function (e) {
      if (!self.interactive) return;
      var fullscreen = document.documentElement.getAttribute('data-display') === 'fullscreen';
      if (!(e.ctrlKey || e.metaKey || fullscreen)) {
        self.hint.classList.add('show');
        clearTimeout(self.hintTimer);
        self.hintTimer = setTimeout(function () { self.hint.classList.remove('show'); }, 1200);
        return;
      }
      e.preventDefault();
      self.zoomBy(e.deltaY < 0 ? 1 : -1, { x: e.offsetX, y: e.offsetY });
    }, { passive: false });

    c.addEventListener('dblclick', function (e) { if (self.interactive) self.zoomBy(1, { x: e.offsetX, y: e.offsetY }); });

    c.addEventListener('keydown', function (e) {
      if (!self.interactive) return;
      var step = 60, handled = true;
      if (e.key === 'ArrowLeft') self.panBy(-step, 0);
      else if (e.key === 'ArrowRight') self.panBy(step, 0);
      else if (e.key === 'ArrowUp') self.panBy(0, -step);
      else if (e.key === 'ArrowDown') self.panBy(0, step);
      else if (e.key === '+' || e.key === '=') self.zoomBy(1);
      else if (e.key === '-' || e.key === '_') self.zoomBy(-1);
      else handled = false;
      if (handled) e.preventDefault();
    });
  };

  proto.panBy = function (dx, dy) {
    var v = this.view;
    var cx = lonToX(v.lon, v.zoom) + dx, cy = latToY(v.lat, v.zoom) + dy;
    this.setView(xToLon(cx, v.zoom), yToLat(cy, v.zoom), v.zoom);
    this.emit('moveend', this.view);
  };

  proto.startGesture = function () {
    var ids = Object.keys(this.pointers);
    var v = this.view;
    this.gesture = { cx: lonToX(v.lon, v.zoom), cy: latToY(v.lat, v.zoom), zoom: v.zoom, pinched: ids.length > 1 || (this.gesture && this.gesture.pinched) };
    if (ids.length === 1) { var p = this.pointers[ids[0]]; this.gesture.x = p.x; this.gesture.y = p.y; }
    else if (ids.length === 2) { var a = this.pointers[ids[0]], b = this.pointers[ids[1]]; this.gesture.dist = Math.hypot(a.x - b.x, a.y - b.y); this.gesture.mx = (a.x + b.x) / 2; this.gesture.my = (a.y + b.y) / 2; }
  };

  proto.updateGesture = function () {
    var g = this.gesture;
    if (!g) return;
    var ids = Object.keys(this.pointers);
    if (ids.length === 1) {
      var p = this.pointers[ids[0]];
      this.setView(xToLon(g.cx - (p.x - g.x), g.zoom), yToLat(g.cy - (p.y - g.y), g.zoom), g.zoom);
    } else if (ids.length === 2) {
      var a = this.pointers[ids[0]], b = this.pointers[ids[1]];
      var dist = Math.hypot(a.x - b.x, a.y - b.y);
      var zoom = clamp(g.zoom + Math.log2(dist / (g.dist || dist)), MIN_ZOOM, MAX_ZOOM);
      var mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
      var wx = g.cx + (g.mx - this.size.w / 2), wy = g.cy + (g.my - this.size.h / 2);
      var lon = xToLon(wx, g.zoom), lat = yToLat(wy, g.zoom);
      var nx = lonToX(lon, zoom) - (mx - this.size.w / 2), ny = latToY(lat, zoom) - (my - this.size.h / 2);
      this.setView(xToLon(nx, zoom), yToLat(ny, zoom), zoom);
      g.pinched = true;
    }
  };

  proto.handleClick = function (x, y) {
    var m = this.hitMarker(x, y);
    if (m) this.emit('markerclick', m);
    else { var ll = this.unproject(x, y); this.emit('click', { longitude: ll.lon, latitude: ll.lat, x: x, y: y }); }
  };

  /** Chooses which map style names are available in a style switcher. */
  MapView.STYLES = STYLES;
  MapView.OVERLAYS = OVERLAYS;
  MapView.lonToX = lonToX;
  MapView.latToY = latToY;

  window.MapView = MapView;
})();
