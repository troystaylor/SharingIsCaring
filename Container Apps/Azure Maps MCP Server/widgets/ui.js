/* Small UI helpers shared by the apps. Exposes window.UI. */
(function () {
  'use strict';

  function el(tag, props, children) {
    var node = document.createElement(tag);
    props = props || {};
    for (var key in props) {
      if (!Object.prototype.hasOwnProperty.call(props, key)) continue;
      var value = props[key];
      if (value === undefined || value === null || value === false) continue;
      if (key === 'class') node.className = value;
      else if (key === 'text') node.textContent = value;
      else if (key === 'html') node.innerHTML = value;
      else if (key === 'style') node.style.cssText = value;
      else if (key === 'on') for (var evt in value) node.addEventListener(evt, value[evt]);
      else node.setAttribute(key, value === true ? '' : value);
    }
    (Array.isArray(children) ? children : children === undefined ? [] : [children]).forEach(function (c) {
      if (c === undefined || c === null || c === false) return;
      node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    });
    return node;
  }

  function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); return node; }

  function debounce(fn, ms) {
    var timer;
    return function () {
      var args = arguments, self = this;
      clearTimeout(timer);
      timer = setTimeout(function () { fn.apply(self, args); }, ms);
    };
  }

  function pad(n) { return (n < 10 ? '0' : '') + n; }

  function fmtDuration(seconds) {
    var s = Math.max(0, Math.round(seconds || 0));
    var h = Math.floor(s / 3600);
    var m = Math.round((s % 3600) / 60);
    if (m === 60) { h += 1; m = 0; }
    if (h >= 24) return Math.floor(h / 24) + ' d ' + (h % 24) + ' h';
    if (h > 0) return h + ' h ' + m + ' min';
    return Math.max(1, m) + ' min';
  }

  function fmtDistance(meters, units) {
    var m = meters || 0;
    if (units === 'imperial') {
      var mi = m / 1609.344;
      return mi >= 0.2 ? (mi >= 10 ? Math.round(mi) : mi.toFixed(1)) + ' mi' : Math.round(m * 3.28084) + ' ft';
    }
    return m >= 1000 ? (m >= 10000 ? Math.round(m / 1000) : (m / 1000).toFixed(1)) + ' km' : Math.round(m) + ' m';
  }

  function fmtArea(sqKm, units) {
    if (units === 'imperial') { var sq = sqKm / 2.58999; return (sq >= 100 ? Math.round(sq) : sq.toFixed(1)).toLocaleString() + ' sq mi'; }
    return (sqKm >= 100 ? Math.round(sqKm) : sqKm.toFixed(1)).toLocaleString() + ' km²';
  }

  /** The clock part of an ISO 8601 string, kept in the string's own offset (not the viewer's). */
  function clockOf(iso) { return iso && iso.length >= 16 ? iso.slice(11, 16) : ''; }
  function dateOf(iso) { return iso ? iso.slice(0, 10) : ''; }

  var WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  function weekdayOf(iso) { var d = new Date(dateOf(iso) + 'T12:00:00Z'); return isNaN(d) ? '' : WEEKDAYS[d.getUTCDay()]; }
  function monthDayOf(iso) { var d = new Date(dateOf(iso) + 'T12:00:00Z'); return isNaN(d) ? '' : MONTHS[d.getUTCMonth()] + ' ' + d.getUTCDate(); }

  function escapeHtml(s) { return String(s === undefined || s === null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  var P = {
    search: 'M8.5 3a5.5 5.5 0 0 1 4.38 8.82l4.15 4.15a.75.75 0 0 1-1.06 1.06l-4.15-4.15A5.5 5.5 0 1 1 8.5 3Zm0 1.5a4 4 0 1 0 0 8 4 4 0 0 0 0-8Z',
    plus: 'M10 3.5a.5.5 0 0 1 .5.5v5.5H16a.5.5 0 0 1 0 1h-5.5V16a.5.5 0 0 1-1 0v-5.5H4a.5.5 0 0 1 0-1h5.5V4a.5.5 0 0 1 .5-.5Z',
    minus: 'M4 10a.5.5 0 0 1 .5-.5h11a.5.5 0 0 1 0 1h-11A.5.5 0 0 1 4 10Z',
    close: 'M4.09 4.22l.06-.07a.5.5 0 0 1 .64-.06l.07.06L10 9.29l5.15-5.14a.5.5 0 0 1 .64-.06l.07.06c.17.17.2.44.06.64l-.06.07L10.71 10l5.15 5.15c.17.17.2.44.06.64l-.06.07a.5.5 0 0 1-.64.06l-.07-.06L10 10.71l-5.15 5.15a.5.5 0 0 1-.64.06l-.07-.06a.5.5 0 0 1-.06-.64l.06-.07L9.29 10 4.15 4.85a.5.5 0 0 1-.06-.63Z',
    expand: 'M4 4h4v1H5v3H4V4Zm8 0h4v4h-1V5h-3V4ZM4 12h1v3h3v1H4v-4Zm11 0h1v4h-4v-1h3v-3Z',
    collapse: 'M8 4v4H4V7h3V4h1Zm4 0h1v3h3v1h-4V4ZM4 12h4v4H7v-3H4v-1Zm8 0h4v1h-3v3h-1v-4Z',
    play: 'M6 4.5v11a.5.5 0 0 0 .77.42l8.5-5.5a.5.5 0 0 0 0-.84l-8.5-5.5A.5.5 0 0 0 6 4.5Z',
    pause: 'M6 4h2a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Zm6 0h2a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z',
    download: 'M10 3a.5.5 0 0 1 .5.5v8.8l2.65-2.65a.5.5 0 0 1 .7.7l-3.5 3.5a.5.5 0 0 1-.7 0l-3.5-3.5a.5.5 0 1 1 .7-.7l2.65 2.65V3.5A.5.5 0 0 1 10 3ZM4 15.5a.5.5 0 0 1 .5-.5h11a.5.5 0 0 1 0 1h-11a.5.5 0 0 1-.5-.5Z',
    copy: 'M7 4a2 2 0 0 1 2-2h5a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-5a2 2 0 0 1-2-2V4Zm2-1a1 1 0 0 0-1 1v7a1 1 0 0 0 1 1h5a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1H9ZM4 7a2 2 0 0 1 1-1.73V12a3 3 0 0 0 3 3h5.73A2 2 0 0 1 12 16H8a4 4 0 0 1-4-4V7Z',
    info: 'M10 2a8 8 0 1 1 0 16 8 8 0 0 1 0-16Zm0 1a7 7 0 1 0 0 14 7 7 0 0 0 0-14Zm0 9.5a.75.75 0 1 1 0 1.5.75.75 0 0 1 0-1.5ZM10 6a.5.5 0 0 1 .5.41V11a.5.5 0 0 1-1 .09V6.5A.5.5 0 0 1 10 6Z',
    warning: 'M8.68 2.79a1.5 1.5 0 0 1 2.64 0l6.5 12A1.5 1.5 0 0 1 16.5 17h-13a1.5 1.5 0 0 1-1.32-2.21l6.5-12ZM10 7a.5.5 0 0 0-.5.5v4a.5.5 0 0 0 1 0v-4A.5.5 0 0 0 10 7Zm0 7.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Z',
    car: 'M5.1 4.5A1.5 1.5 0 0 1 6.5 3.5h7c.6 0 1.15.37 1.4.93l1.3 3.07H17a.5.5 0 0 1 0 1h-.5v6a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1v-1h-6v1a1 1 0 0 1-1 1h-1a1 1 0 0 1-1-1v-6H3a.5.5 0 0 1 0-1h.8l1.3-3.07ZM6.5 4.5l-1.3 3h9.6l-1.3-3h-7ZM5 9.5v3a.5.5 0 0 0 .5.5h9a.5.5 0 0 0 .5-.5v-3H5Z',
    walk: 'M11 3.5a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0ZM8.1 6.2l2.2-.5c.5-.1 1 .2 1.2.7l.7 2 1.8.6a.5.5 0 1 1-.3.95l-2-.7a.9.9 0 0 1-.55-.55l-.3-.8-.8 3.2 1.9 2.2c.1.1.15.25.15.4V17a.5.5 0 0 1-1 0v-2.35l-1.5-1.7-.9 3.55a.5.5 0 0 1-.97-.25l1.6-6.3-1.05.2-.6 1.7a.5.5 0 0 1-.94-.34l.7-2A1 1 0 0 1 8.1 6.2Z',
    truck: 'M2 6a1.5 1.5 0 0 1 1.5-1.5h7A1.5 1.5 0 0 1 12 6v1h2.1c.4 0 .78.2 1 .53L17 10.2c.2.3.3.6.3.95V13a1 1 0 0 1-1 1H15.5a1.75 1.75 0 0 1-3.5 0H8a1.75 1.75 0 0 1-3.5 0H3.5A1.5 1.5 0 0 1 2 12.5V6Zm10 2v2.5h3.7l-1.6-2.5H12ZM6.25 13a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Zm7.5 0a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5Z',
    refresh: 'M10 4a6 6 0 0 1 5.33 3.25.5.5 0 0 1-.9.44A5 5 0 1 0 15 10a.5.5 0 0 1 1 0 6 6 0 1 1-6-6Zm4.5-1a.5.5 0 0 1 .5.5V7h-3.5a.5.5 0 0 1 0-1H14V3.5a.5.5 0 0 1 .5-.5Z',
    print: 'M6 3h8a1 1 0 0 1 1 1v2h1a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-1v2a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h1V4a1 1 0 0 1 1-1Zm0 1v2h8V4H6Zm0 8v4h8v-4H6Z',
    pin: 'M10 2a5 5 0 0 1 5 5c0 2.6-3.3 7.1-4.45 8.57a.7.7 0 0 1-1.1 0C8.3 14.1 5 9.6 5 7a5 5 0 0 1 5-5Zm0 3.2a1.8 1.8 0 1 0 0 3.6 1.8 1.8 0 0 0 0-3.6Z',
    layers: 'M10 2.5l7 3.8-7 3.8-7-3.8 7-3.8Zm-6.3 7.6L10 13.6l6.3-3.5.7 1.2-7 3.8-7-3.8.7-1.2Zm0 3.5L10 17l6.3-3.4.7 1.2-7 3.8-7-3.8.7-1.2Z'
  };

  function icon(name, size) {
    var s = size || 20;
    return '<svg width="' + s + '" height="' + s + '" viewBox="0 0 20 20" aria-hidden="true" focusable="false"><path d="' + (P[name] || '') + '"/></svg>';
  }

  function iconButton(name, label, onClick, extraClass) {
    return el('button', { class: 'btn subtle ' + (extraClass || ''), type: 'button', 'aria-label': label, title: label, html: icon(name), on: { click: onClick } });
  }

  function message(kind, text) {
    return el('div', { class: 'message ' + (kind || ''), role: kind === 'error' ? 'alert' : 'status', html: icon(kind === 'error' || kind === 'warning' ? 'warning' : 'info') }, [el('span', { text: text })]);
  }

  // Weather icons. Azure Maps uses the AccuWeather icon codes (1-44).
  var GROUPS = {
    sun: [1, 2, 30], moon: [33, 34], partlySun: [3, 4, 5], partlyMoon: [35, 36, 37], cloud: [6, 7, 8, 38], fog: [11], rain: [12, 13, 14, 18, 39, 40],
    storm: [15, 16, 17, 41, 42], snow: [19, 20, 21, 22, 23, 43, 44], sleet: [24, 25, 26, 29], wind: [32], cold: [31]
  };
  function weatherGroup(code, isDay) {
    for (var g in GROUPS) if (GROUPS[g].indexOf(Number(code)) >= 0) return g;
    return isDay === false ? 'moon' : 'cloud';
  }

  var CLOUD = '<path d="M7 17.5h10.2a3.8 3.8 0 0 0 .6-7.56 5.5 5.5 0 0 0-10.5-1.2A4.4 4.4 0 0 0 7 17.5Z" fill="#9aa5b1" stroke="#7b8794" stroke-width="0.8"/>';
  var SUN = '<circle cx="12" cy="12" r="4.2" fill="#ffb900"/><g stroke="#ffb900" stroke-width="1.6" stroke-linecap="round"><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M5.6 18.4 7 17M17 7l1.4-1.4"/></g>';
  var MOON = '<path d="M16.5 14.5A6.5 6.5 0 0 1 9.5 5.2a6.5 6.5 0 1 0 8.3 8.3 6.5 6.5 0 0 1-1.3 1Z" fill="#c7d0dc" stroke="#9aa5b1" stroke-width="0.6"/>';
  var DROPS = '<g stroke="#3a8dde" stroke-width="1.6" stroke-linecap="round"><path d="M9 19.5l-.8 2M13 19.5l-.8 2M17 19.5l-.8 2"/></g>';
  var FLAKES = '<g fill="#7fb9e8"><circle cx="9" cy="20.2" r="1.1"/><circle cx="13" cy="21.2" r="1.1"/><circle cx="17" cy="20.2" r="1.1"/></g>';
  var BOLT = '<path d="M13 15l-3 4h2.2l-1 3.2 3.6-4.6h-2.2L14.6 15H13Z" fill="#ffb900" stroke="#c77700" stroke-width="0.5"/>';
  var PARTIAL_CLOUD = '<path d="M9 19h9a3.4 3.4 0 0 0 .5-6.77 4.9 4.9 0 0 0-9.4-1A3.9 3.9 0 0 0 9 19Z" fill="#aab4bf" stroke="#7b8794" stroke-width="0.8"/>';

  function weatherIcon(code, isDay, size) {
    var s = size || 40;
    var g = weatherGroup(code, isDay);
    var body;
    var shift = '<g transform="translate(0,-3)">';
    switch (g) {
      case 'sun': body = SUN; break;
      case 'moon': body = MOON; break;
      case 'partlySun': body = '<g transform="translate(-3,-3) scale(0.8)">' + SUN + '</g>' + PARTIAL_CLOUD; break;
      case 'partlyMoon': body = '<g transform="translate(-3,-3) scale(0.8)">' + MOON + '</g>' + PARTIAL_CLOUD; break;
      case 'rain': body = shift + CLOUD + '</g>' + DROPS; break;
      case 'storm': body = shift + CLOUD + '</g>' + BOLT; break;
      case 'snow': body = shift + CLOUD + '</g>' + FLAKES; break;
      case 'sleet': body = shift + CLOUD + '</g>' + DROPS + FLAKES.replace('cy="20.2"', 'cy="23"'); break;
      case 'fog': body = CLOUD.replace('M7 17.5', 'M7 15') + '<g stroke="#9aa5b1" stroke-width="1.6" stroke-linecap="round"><path d="M5 18.5h14M7 21.5h10"/></g>'; break;
      case 'wind': body = '<g stroke="#7b8794" stroke-width="1.8" stroke-linecap="round" fill="none"><path d="M4 9h10a2.5 2.5 0 1 0-2.5-2.5M4 13h14a2.5 2.5 0 1 1-2.5 2.5M4 17h7"/></g>'; break;
      case 'cold': body = '<g stroke="#3a8dde" stroke-width="1.6" stroke-linecap="round"><path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9"/></g>'; break;
      default: body = CLOUD;
    }
    return '<svg width="' + s + '" height="' + s + '" viewBox="0 0 24 24" role="img" aria-label="' + g + '">' + body + '</svg>';
  }

  function toCsv(rows, columns) {
    function cell(v) {
      var t = v === undefined || v === null ? '' : String(v);
      if (/^[=+\-@\t\r]/.test(t)) t = "'" + t;
      return /[",\n\r]/.test(t) ? '"' + t.replace(/"/g, '""') + '"' : t;
    }
    var lines = [columns.map(function (c) { return cell(c.label); }).join(',')];
    rows.forEach(function (r) { lines.push(columns.map(function (c) { return cell(typeof c.get === 'function' ? c.get(r) : r[c.key]); }).join(',')); });
    return lines.join('\r\n') + '\r\n';
  }

  /** Copies text with the clipboard API when the host allows it, otherwise with a temporary text area. */
  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return legacyCopy(text); });
    return Promise.resolve(legacyCopy(text));
  }
  function legacyCopy(text) {
    var ta = el('textarea', { style: 'position:fixed;left:-9999px' });
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    var okay = false;
    try { okay = document.execCommand('copy'); } catch (e) { okay = false; }
    document.body.removeChild(ta);
    return okay;
  }

  function tempHint(text, ms) {
    var node = el('div', { class: 'card', style: 'position:fixed;left:50%;bottom:12px;transform:translateX(-50%);padding:6px 12px;z-index:10', role: 'status', text: text });
    document.body.appendChild(node);
    setTimeout(function () { if (node.parentNode) node.parentNode.removeChild(node); }, ms || 2200);
  }

  window.UI = {
    el: el, clear: clear, debounce: debounce, icon: icon, iconButton: iconButton, message: message, escapeHtml: escapeHtml,
    fmtDuration: fmtDuration, fmtDistance: fmtDistance, fmtArea: fmtArea, clockOf: clockOf, dateOf: dateOf, weekdayOf: weekdayOf, monthDayOf: monthDayOf,
    weatherIcon: weatherIcon, toCsv: toCsv, copyText: copyText, tempHint: tempHint, pad: pad
  };
})();
