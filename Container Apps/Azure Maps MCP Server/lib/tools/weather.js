'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult, jsonText } = require('../define');
const { str, num, int, bool, enumOf, arrayOf, strings, place } = require('../schema');
const { InputError, intInRange, oneOf, list, labelOf } = require('../args');
const { haversine, simplifyLine, roundLine, round, bboxOf, formatDuration } = require('../geo');
const route = require('./route');

const LANGUAGE = str('Language for descriptions as an IETF tag, for example en-US.');
const UNIT = enumOf('Units: metric (default) or imperial.', ['metric', 'imperial']);
const LOCATION = place('The place to get weather for. Accepts an address, a place name or coordinates.');
const q = (p) => [p.latitude, p.longitude];
const val = (m) => (m && m.value !== undefined ? m.value : undefined);
const withUnit = (m) => (m && m.value !== undefined ? { value: m.value, unit: m.unit } : undefined);
const show = (m) => (m && m.value !== undefined ? `${m.value}${m.unit && /^[CF]$/.test(m.unit) ? `°${m.unit}` : ` ${m.unit || ''}`.trimEnd()}` : '?');
const clean = (o) => Object.fromEntries(Object.entries(o).filter(([, v]) => v !== undefined && v !== null));

const windOf = (w) => (w ? clean({ direction: w.direction && w.direction.localizedDescription, degrees: w.direction && w.direction.degrees, speed: withUnit(w.speed) }) : undefined);

function normalizeCurrent(r) {
  return clean({
    time: r.dateTime, phrase: r.phrase, iconCode: r.iconCode, isDay: r.isDayTime, hasPrecipitation: r.hasPrecipitation,
    precipitationType: r.precipitationType, temperature: withUnit(r.temperature), realFeel: withUnit(r.realFeelTemperature),
    humidity: r.relativeHumidity, dewPoint: withUnit(r.dewPoint), wind: windOf(r.wind), gust: r.windGust && withUnit(r.windGust.speed),
    uvIndex: r.uvIndex, uvPhrase: r.uvIndexPhrase, visibility: withUnit(r.visibility), cloudCover: r.cloudCover,
    pressure: withUnit(r.pressure), pressureTrend: r.pressureTendency && r.pressureTendency.localizedDescription,
    precipitationPast24h: r.precipitationSummary && withUnit(r.precipitationSummary.past24Hours),
    high24: r.temperatureSummary && r.temperatureSummary.past24Hours && withUnit(r.temperatureSummary.past24Hours.maximum),
    low24: r.temperatureSummary && r.temperatureSummary.past24Hours && withUnit(r.temperatureSummary.past24Hours.minimum)
  });
}

function normalizeHour(h) {
  return clean({
    time: h.date, phrase: h.iconPhrase, iconCode: h.iconCode, isDay: h.isDaylight, temperature: withUnit(h.temperature), realFeel: withUnit(h.realFeelTemperature),
    precipitationProbability: h.precipitationProbability, rain: withUnit(h.totalLiquid), snow: withUnit(h.snow), wind: windOf(h.wind), gust: h.windGust && withUnit(h.windGust.speed),
    humidity: h.relativeHumidity, uvIndex: h.uvIndex, cloudCover: h.cloudCover
  });
}

function normalizeDay(d) {
  const part = (p) => p && clean({ phrase: p.shortPhrase, iconCode: p.iconCode, iconPhrase: p.iconPhrase, precipitationProbability: p.precipitationProbability, precipitationType: p.precipitationType, wind: windOf(p.wind), gust: p.windGust && withUnit(p.windGust.speed), rain: withUnit(p.totalLiquid), snow: withUnit(p.snow) });
  return clean({
    date: d.date, min: withUnit(d.temperature && d.temperature.minimum), max: withUnit(d.temperature && d.temperature.maximum),
    realFeelMin: withUnit(d.realFeelTemperature && d.realFeelTemperature.minimum), realFeelMax: withUnit(d.realFeelTemperature && d.realFeelTemperature.maximum),
    hoursOfSun: d.hoursOfSun, day: part(d.day), night: part(d.night),
    airAndPollen: (d.airAndPollen || []).filter((a) => a.name !== 'UVIndex').map((a) => ({ name: a.name, value: a.value, category: a.category })).slice(0, 6)
  });
}

const QUARTERS = ['morning', 'afternoon', 'evening', 'overnight'];

function normalizeQuarter(d) {
  return clean({
    time: d.effectiveDate || d.date, period: QUARTERS[d.quarter], phrase: d.phrase, iconCode: d.iconCode,
    min: withUnit(d.temperature && d.temperature.minimum), max: withUnit(d.temperature && d.temperature.maximum),
    precipitationProbability: d.precipitationProbability, rain: withUnit(d.rain), snow: withUnit(d.snow), wind: windOf(d.wind), cloudCover: d.cloudCover
  });
}

function normalizeAlert(a) {
  const areas = (a.alertAreas || []).map((x) => clean({
    name: x.name, start: x.startTime, end: x.endTime, status: x.latestStatus && (x.latestStatus.english || x.latestStatus.localized),
    summary: x.summary, details: x.alertDetails && x.alertDetails.length ? x.alertDetails : undefined
  }));
  return clean({
    id: a.alertId, description: (a.description && (a.description.localized || a.description.english)) || '', category: a.category, priority: a.priority, level: a.level,
    source: a.source, areas, disclaimer: a.disclaimer
  });
}

function normalizeAir(r) {
  return clean({
    time: r.dateTime, index: r.index, globalIndex: r.globalIndex, category: r.category, color: r.categoryColor, dominantPollutant: r.dominantPollutant, description: r.description,
    pollutants: (r.pollutants || []).map((p) => clean({ type: p.type, name: p.name, index: p.index, concentration: withUnit(p.concentration) }))
  });
}

function normalizeMinute(m) {
  return {
    summary: m.summary && (m.summary.briefPhrase || m.summary.shortPhrase),
    iconCode: m.summary && m.summary.iconCode,
    intervals: (m.intervals || []).map((i) => clean({ time: i.startTime, minute: i.minute, dbz: i.dbz, phrase: i.shortPhrase, threshold: i.threshold, color: i.simplifiedColor && i.simplifiedColor.hex, type: i.precipitationType }))
  };
}

const tools = [];

async function weather(ctx, path, args, query = {}, where) {
  const loc = where || await ctx.places.resolve(args.location, 'location');
  const data = await ctx.maps.json(path, { version: VERSIONS.weather, query: { query: q(loc), unit: args.unit, language: args.language, ...query } });
  return { data, loc };
}

const lineOf = (m) => (m ? show(m) : '?');

tools.push(defineTool({
  name: 'get_weather',
  title: 'Get a weather overview',
  description: 'Get a full weather picture for a place in one call: current conditions, the next 12 hours, a 5-day forecast, rain in the next two hours when available, active severe weather alerts and air quality. Accepts an address, place name or coordinates. Shows a weather dashboard. For one specific forecast type, use the other weather tools.',
  properties: { location: LOCATION, unit: UNIT, language: LANGUAGE },
  required: ['location'],
  widget: 'weather',
  async handler(args, ctx) {
    const loc = await ctx.places.resolve(args.location, 'location');
    const get = async (path, query) => { try { return (await weather(ctx, path, args, query, loc)).data; } catch (err) { return { error: err.message }; } };
    const [cur, hourly, daily, alerts, air, minute] = await Promise.all([
      get('/weather/currentConditions/json', { details: true }),
      get('/weather/forecast/hourly/json', { duration: 12 }),
      get('/weather/forecast/daily/json', { duration: 5 }),
      get('/weather/severe/alerts/json', { details: true }),
      get('/weather/airQuality/current/json', { pollutants: true }),
      get('/weather/forecast/minute/json', { interval: 15, unit: undefined })
    ]);
    if (cur.error && hourly.error && daily.error) throw new Error(cur.error);

    const current = cur.results && cur.results[0] ? normalizeCurrent(cur.results[0]) : undefined;
    const hours = (hourly.forecasts || []).map(normalizeHour);
    const days = (daily.forecasts || []).map(normalizeDay);
    const alertList = (alerts.results || []).map(normalizeAlert);
    const airQuality = air.results && air.results[0] ? normalizeAir(air.results[0]) : undefined;
    const nowcast = minute.intervals ? normalizeMinute(minute) : undefined;

    const parts = [`Weather for ${labelOf(loc)}:`];
    if (current) parts.push(`Now: ${current.phrase}, ${lineOf(current.temperature)} (feels like ${lineOf(current.realFeel)}), humidity ${current.humidity}%, wind ${current.wind ? `${current.wind.direction || ''} ${lineOf(current.wind.speed)}` : '?'}, UV ${current.uvIndex ?? '?'}.`);
    if (nowcast && nowcast.summary) parts.push(`Next 2 hours: ${nowcast.summary}.`);
    if (hours.length) parts.push(`Next hours: ${hours.slice(0, 6).map((h) => `${h.time.slice(11, 16)} ${lineOf(h.temperature)} ${h.phrase || ''} ${h.precipitationProbability ?? 0}%`).join('; ')}.`);
    if (days.length) parts.push(`Forecast: ${days.map((d) => `${d.date.slice(5, 10)} ${lineOf(d.min)}–${lineOf(d.max)} ${d.day && d.day.phrase ? d.day.phrase : ''}`).join('; ')}.`);
    parts.push(alertList.length ? `Alerts: ${alertList.map((a) => a.description).join('; ')}.` : 'No active severe weather alerts.');
    if (airQuality) parts.push(`Air quality: ${airQuality.category} (index ${airQuality.index}), dominant pollutant ${airQuality.dominantPollutant || 'n/a'}.`);
    parts.push('A weather dashboard is shown when the client supports it.');

    return ok(parts.join('\n'), {
      kind: 'weather', location: { longitude: loc.longitude, latitude: loc.latitude, name: labelOf(loc) }, unit: args.unit || 'metric',
      current, hourly: hours, daily: days, alerts: alertList, airQuality, nowcast, errors: [cur, hourly, daily].filter((x) => x.error).map((x) => x.error)
    });
  }
}));

tools.push(defineTool({
  name: 'get_current_conditions',
  title: 'Get current weather conditions',
  description: 'Get the current weather at a place: temperature, what it feels like, wind, humidity, UV, visibility, pressure and precipitation. Use duration 6 or 24 for the past hours.',
  properties: { location: LOCATION, unit: UNIT, details: bool('Include full details such as wind chill and precipitation summaries. Default true.'), duration: int('0 for now (default), 6 or 24 for the past hours.'), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/currentConditions/json', args, { details: args.details === false ? false : true, duration: args.duration });
    const rows = (data.results || []).map(normalizeCurrent);
    if (!rows.length) return ok(`No current conditions are available for ${labelOf(loc)}.`);
    const c = rows[0];
    return dataResult(`Current weather at ${labelOf(loc)}: ${c.phrase}, ${lineOf(c.temperature)}, feels like ${lineOf(c.realFeel)}.`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_hourly_forecast',
  title: 'Get the hourly forecast',
  description: 'Get the hour-by-hour forecast for a place for the next 1, 12, 24, 72, 120 or 240 hours: temperature, conditions, chance of precipitation and wind.',
  properties: { location: LOCATION, duration: enumOf('Hours to forecast. Default 12.', ['1', '12', '24', '72', '120', '240']), unit: UNIT, language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/forecast/hourly/json', args, { duration: args.duration || 12 });
    const rows = (data.forecasts || []).map(normalizeHour);
    return dataResult(`Hourly forecast for ${labelOf(loc)}, ${rows.length} hour(s).`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_daily_forecast',
  title: 'Get the daily forecast',
  description: 'Get the day-by-day forecast for a place for the next 1, 5, 10, 25 or 45 days: high and low, day and night conditions, chance of precipitation and wind.',
  properties: { location: LOCATION, duration: enumOf('Days to forecast. Default 5.', ['1', '5', '10', '25', '45']), unit: UNIT, language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/forecast/daily/json', args, { duration: args.duration || 5 });
    const rows = (data.forecasts || []).map(normalizeDay);
    return dataResult(`Daily forecast for ${labelOf(loc)}, ${rows.length} day(s).${data.summary && data.summary.phrase ? ` Summary: ${data.summary.phrase}` : ''}`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_quarter_day_forecast',
  title: 'Get the quarter-day forecast',
  description: 'Get the forecast in six-hour blocks (morning, afternoon, evening, overnight) for the next 1, 5, 10 or 15 days.',
  properties: { location: LOCATION, duration: enumOf('Days to forecast. Default 5.', ['1', '5', '10', '15']), unit: UNIT, language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/forecast/quarterDay/json', args, { duration: args.duration || 5 });
    return dataResult(`Quarter-day forecast for ${labelOf(loc)}.`, (data.forecasts || []).map(normalizeQuarter));
  }
}));

tools.push(defineTool({
  name: 'get_minute_forecast',
  title: 'Will it rain in the next two hours?',
  description: 'Get a minute-by-minute precipitation forecast for the next 120 minutes at a place, with a plain-language summary such as "rain starting in 12 minutes".',
  properties: { location: LOCATION, interval: enumOf('Minutes per interval. Default 15.', ['1', '5', '15']), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/forecast/minute/json', args, { interval: args.interval || 15, unit: undefined });
    const m = normalizeMinute(data);
    return dataResult(`Next two hours at ${labelOf(loc)}: ${m.summary || 'no summary'}.`, m);
  }
}));

tools.push(defineTool({
  name: 'get_severe_weather_alerts',
  title: 'Get severe weather alerts',
  description: 'Get active severe weather alerts such as storms, flooding, heat waves, wildfires and winter weather for a place.',
  properties: { location: LOCATION, details: bool('Include full alert text for each area. Default true.'), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/severe/alerts/json', args, { details: args.details === false ? false : true, unit: undefined });
    const alerts = (data.results || []).map(normalizeAlert);
    return dataResult(alerts.length ? `${alerts.length} active alert(s) for ${labelOf(loc)}.` : `No active severe weather alerts for ${labelOf(loc)}.`, alerts);
  }
}));

tools.push(defineTool({
  name: 'get_current_air_quality',
  title: 'Get current air quality',
  description: 'Get the current air quality index, category, dominant pollutant and health guidance for a place, with pollutant levels.',
  properties: { location: LOCATION, pollutants: bool('Include each pollutant. Default true.'), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/airQuality/current/json', args, { pollutants: args.pollutants === false ? false : true, unit: undefined });
    const rows = (data.results || []).map(normalizeAir);
    return dataResult(rows[0] ? `Air quality at ${labelOf(loc)}: ${rows[0].category} (index ${rows[0].index}).` : `No air quality data for ${labelOf(loc)}.`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_air_quality_hourly',
  title: 'Get the hourly air quality forecast',
  description: 'Get the hourly air quality forecast for the next 1, 12, 24, 48, 72 or 96 hours.',
  properties: { location: LOCATION, duration: enumOf('Hours to forecast. Default 24.', ['1', '12', '24', '48', '72', '96']), pollutants: bool('Include each pollutant. Default false.'), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/airQuality/forecasts/hourly/json', args, { duration: args.duration || 24, pollutants: args.pollutants === true, unit: undefined });
    return dataResult(`Hourly air quality forecast for ${labelOf(loc)}.`, (data.results || []).map(normalizeAir));
  }
}));

tools.push(defineTool({
  name: 'get_air_quality_daily',
  title: 'Get the daily air quality forecast',
  description: 'Get the daily air quality forecast for the next 1 to 7 days.',
  properties: { location: LOCATION, duration: enumOf('Days to forecast. Default 5.', ['1', '2', '3', '4', '5', '6', '7']), language: LANGUAGE },
  required: ['location'],
  async handler(args, ctx) {
    const { data, loc } = await weather(ctx, '/weather/airQuality/forecasts/daily/json', args, { duration: args.duration || 5, unit: undefined });
    return dataResult(`Daily air quality forecast for ${labelOf(loc)}.`, (data.results || []).map(normalizeAir));
  }
}));

tools.push(defineTool({
  name: 'get_daily_indices',
  title: 'Get activity weather indices',
  description: 'Find out whether the weather is good for an activity, such as running, golf, construction, farming, outdoor dining or flying a kite. Returns indices with a category and a description for each day.',
  properties: {
    location: LOCATION, duration: enumOf('Days, 1 to 15. Default 1.', ['1', '5', '10', '15']),
    indexId: int('Numeric ID of a single index to return. Leave blank to get all indices. Cannot be combined with indexGroupId.'),
    indexGroupId: int('Numeric ID of an index group to return. Cannot be combined with indexId.'), language: LANGUAGE
  },
  required: ['location'],
  async handler(args, ctx) {
    if (args.indexId && args.indexGroupId) throw new InputError('Use indexId or indexGroupId, not both.');
    const { data, loc } = await weather(ctx, '/weather/indices/daily/json', args, { duration: args.duration || 1, indexId: args.indexId, indexGroupId: args.indexGroupId, unit: undefined });
    const rows = (data.results || []).map((r) => clean({ index: r.indexName, indexId: r.indexId, date: r.dateTime, value: r.value, category: r.category, description: r.description }));
    return dataResult(`${rows.length} index value(s) for ${labelOf(loc)}.`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_historical_weather',
  title: 'Get historical weather and climate normals',
  description: 'Get past weather for a place and date range: what was observed (actuals), what is typical for the dates (normals), or the record highs and lows (records). Dates are YYYY-MM-DD.',
  properties: {
    location: LOCATION, kind: enumOf('actuals (observed), normals (typical) or records. Default actuals.', ['actuals', 'normals', 'records']),
    startDate: str('First date, YYYY-MM-DD.'), endDate: str('Last date, YYYY-MM-DD.'), unit: UNIT
  },
  required: ['location', 'startDate', 'endDate'],
  async handler(args, ctx) {
    const kind = oneOf(args.kind, 'kind', ['actuals', 'normals', 'records'], 'actuals');
    for (const k of ['startDate', 'endDate']) if (!/^\d{4}-\d{2}-\d{2}$/.test(args[k])) throw new InputError(`${k} must be YYYY-MM-DD.`);
    const { data, loc } = await weather(ctx, `/weather/historical/${kind}/daily/json`, args, { startDate: args.startDate, endDate: args.endDate, language: undefined });
    const rows = (data.results || []).map((r) => clean({
      date: r.date, min: withUnit(r.temperature && r.temperature.minimum), max: withUnit(r.temperature && r.temperature.maximum), average: withUnit(r.temperature && r.temperature.average),
      precipitation: withUnit(r.precipitation), snowfall: withUnit(r.snowfall), snowDepth: withUnit(r.snowDepth), heatingDegreeDays: withUnit(r.degreeDaySummary && r.degreeDaySummary.heating), coolingDegreeDays: withUnit(r.degreeDaySummary && r.degreeDaySummary.cooling),
      ...(kind === 'records' ? { recordYears: { min: r.temperature && r.temperature.minimum && r.temperature.minimum.year, max: r.temperature && r.temperature.maximum && r.temperature.maximum.year } } : {})
    }));
    return dataResult(`Historical ${kind} for ${labelOf(loc)}, ${rows.length} day(s).`, rows);
  }
}));

// ───────────────────────────── tropical storms ─────────────────────────────

const BASINS = ['AL', 'CP', 'EP', 'NI', 'NP', 'SI', 'SP'];

function stormPoint(r) {
  const knots = (m) => (m && m.value !== undefined ? m : undefined);
  return clean({
    time: r.dateTime, longitude: r.location && r.location.longitude, latitude: r.location && r.location.latitude, status: r.status, intensity: r.intensity,
    sustainedWind: withUnit(knots(r.maxWindSpeed || r.sustainedWind)), gust: withUnit(knots(r.maxWindGust || r.windGust)), pressure: withUnit(r.minimumPressure),
    movement: r.movement && clean({ direction: r.movement.direction && r.movement.direction.localizedDescription, speed: withUnit(r.movement.speed) }),
    windRadii: r.windRadiiSummary && r.windRadiiSummary.map ? r.windRadiiSummary.map((w) => clean({ windSpeed: w.windSpeed, radii: w.radiusSectorData && w.radiusSectorData.map((s) => ({ begin: s.beginBearing, end: s.endBearing, range: s.range })) })).slice(0, 4) : undefined
  });
}

tools.push(defineTool({
  name: 'get_tropical_storms',
  title: 'Track tropical storms and hurricanes',
  description: 'Find tropical storms, hurricanes, cyclones and typhoons from national weather agencies. scope active lists storms happening now and includes the past track and forecast track of the strongest ones; search finds storms by year, basin and government ID; locations and forecast return the past positions and the forecast track of one storm; track returns both. Shows the storms and their tracks on a map.',
  properties: {
    scope: enumOf('active (default), search, locations, forecast or track.', ['active', 'search', 'locations', 'forecast', 'track']),
    year: int('Year of the storm. Required for search, locations, forecast and track.'),
    basinId: enumOf('Ocean basin: AL Atlantic, CP Central Pacific, EP East Pacific, NI North Indian, NP Northwest Pacific, SI South Indian, SP South Pacific.', BASINS),
    govId: int('Government storm ID. Required for locations, forecast and track.'),
    maxStorms: int('For active: how many storms to include tracks for, 1 to 8. Default 5.', { minimum: 1, maximum: 8 }),
    unit: UNIT
  },
  widget: 'storms',
  async handler(args, ctx) {
    const scope = oneOf(args.scope, 'scope', ['active', 'search', 'locations', 'forecast', 'track'], 'active');
    const unit = args.unit;

    const trackOf = async (s) => {
      const base = { year: s.year, basinId: s.basinId, govId: s.govId, unit, details: true };
      const [past, fut] = await Promise.all([
        ctx.maps.json('/weather/tropical/storms/locations/json', { version: VERSIONS.weather, query: base }).catch(() => ({})),
        ctx.maps.json('/weather/tropical/storms/forecasts/json', { version: VERSIONS.weather, query: base }).catch(() => ({}))
      ]);
      return { past: (past.results || []).map(stormPoint), forecast: (fut.results || []).map(stormPoint) };
    };
    const storm = (s) => clean({ name: s.name, year: s.year, basinId: s.basinId, govId: s.govId, isActive: s.isActive, isSubtropical: s.isSubtropical, startDate: s.startDate, endDate: s.endDate });

    if (scope === 'active' || scope === 'search') {
      const data = scope === 'active'
        ? await ctx.maps.json('/weather/tropical/storms/active/json', { version: VERSIONS.weather })
        : await ctx.maps.json('/weather/tropical/storms/json', { version: VERSIONS.weather, query: { year: args.year || new Date().getFullYear(), basinId: args.basinId, govId: args.govId } });
      const storms = (data.results || []).map(storm);
      const max = intInRange(args.maxStorms, 'maxStorms', 1, 8, 5);
      const withTracks = await Promise.all(storms.slice(0, max).map(async (s) => ({ ...s, ...(await trackOf(s)) })));
      const result = [...withTracks, ...storms.slice(max)];
      const lines = result.map((s) => `${s.name} (${s.basinId} ${s.year}, ID ${s.govId})${s.isActive ? ' active' : ''}${s.past && s.past.length ? `, last seen ${s.past[s.past.length - 1].latitude},${s.past[s.past.length - 1].longitude}` : ''}`);
      return ok(`${storms.length} ${scope === 'active' ? 'active ' : ''}tropical storm(s).${lines.length ? `\n${lines.join('\n')}` : ''}${scope === 'active' && !storms.length ? '\nNo tropical storms are active right now.' : ''}`, { kind: 'storms', storms: result, unit: unit || 'metric', scope });
    }

    for (const k of ['year', 'basinId', 'govId']) if (!args[k]) throw new InputError(`${k} is required for scope ${scope}.`);
    const s = { year: args.year, basinId: args.basinId, govId: args.govId };
    const t = await trackOf(s);
    const out = { ...s, ...(scope === 'locations' ? { past: t.past } : scope === 'forecast' ? { forecast: t.forecast } : t) };
    return ok(`Storm ${args.basinId} ${args.year} (ID ${args.govId}): ${t.past.length} past position(s), ${t.forecast.length} forecast position(s).\n${jsonText({ past: t.past.slice(-12), forecast: t.forecast.slice(0, 12) }).text}`, { kind: 'storms', storms: [out], unit: unit || 'metric', scope });
  }
}));

// ───────────────────────────── weather along a route ─────────────────────────────

const MAX_WAYPOINTS = 60;
const MAX_ETA_MINUTES = 120;

tools.push(defineTool({
  name: 'get_weather_along_route',
  title: 'Get the weather along a route',
  description: 'Check the weather you will meet along a drive: rain, snow, ice, fog, wind and sun glare at each point of the route, timed to when you will get there, with hazard warnings. Accepts addresses or coordinates, and calculates the route for you. Azure Maps covers the next 120 minutes of travel, so longer trips are checked for their first two hours. Shows the route colored by hazard with a timeline.',
  properties: {
    origin: place('Where the trip starts.'), destination: place('Where the trip ends.'),
    leaveInMinutes: int('Minutes from now until you leave, 0 to 119. Default 0.', { minimum: 0, maximum: 119 }),
    language: LANGUAGE
  },
  required: ['origin', 'destination'],
  widget: 'routeweather',
  async handler(args, ctx) {
    const stops = await route.resolveStops(args, ctx);
    const json = await route.fetchDirections(ctx, { stops, args: { travelMode: 'driving', optimizeRoute: 'fastestWithTraffic', language: args.language }, outputs: ['routeSummary', 'routePath'], maxRouteCount: 1 });
    const best = route.parseRoutes(json, { lineMax: 3000, stepMax: 0 })[0];
    if (!best) throw new InputError('No route was found between those places.');

    const delay = args.leaveInMinutes || 0;
    const full = best.lineFull;
    const cum = [0];
    for (let i = 1; i < full.length; i++) cum.push(cum[i - 1] + haversine(full[i - 1], full[i]));
    const total = cum[cum.length - 1] || 1;
    const duration = (best.trafficSeconds ?? best.seconds) / 60;
    const etaAt = (d) => delay + (d / total) * duration;

    const lastIndex = (() => { let i = full.length - 1; while (i > 0 && etaAt(cum[i]) > MAX_ETA_MINUTES) i--; return i; })();
    if (etaAt(0) > MAX_ETA_MINUTES) throw new InputError('Leave within the next two hours: Azure Maps forecasts only 120 minutes ahead.');
    const covered = Math.max(2, lastIndex + 1);
    const count = Math.min(MAX_WAYPOINTS, Math.max(2, Math.ceil(((cum[covered - 1] || total) / 1000) / 5) + 1));
    const picks = [];
    for (let k = 0; k < count; k++) {
      const target = ((cum[covered - 1] || total) * k) / (count - 1);
      let i = 0; while (i < covered - 1 && cum[i] < target) i++;
      if (!picks.length || picks[picks.length - 1] !== i) picks.push(i);
    }
    const wps = picks.map((i) => ({ longitude: full[i][0], latitude: full[i][1], eta: Math.min(MAX_ETA_MINUTES, round(etaAt(cum[i]), 1)), km: round(cum[i] / 1000, 1) }));
    if (wps.length < 2) wps.push({ ...wps[0], km: wps[0].km + 0.1 });

    const data = await ctx.maps.json('/weather/route/json', {
      version: VERSIONS.weather, query: { query: wps.map((w) => `${round(w.latitude, 5)},${round(w.longitude, 5)},${w.eta}`).join(':'), language: args.language }
    });
    const points = (data.waypoints || []).map((w, i) => clean({
      ...wps[i], iconCode: w.iconCode, phrase: w.shortPhrase, isDay: w.isDayTime, cloudCover: w.cloudCover, temperature: withUnit(w.temperature), wind: windOf(w.wind), gust: w.windGust && withUnit(w.windGust.speed),
      precipitation: w.precipitation && clean({ type: w.precipitation.type, dbz: w.precipitation.dbz }), lightning: w.lightningCount, sunGlare: w.sunGlare && w.sunGlare.glareIndex,
      hazardIndex: w.hazards && w.hazards.maxHazardIndex, hazards: ((w.hazards && w.hazards.hazardDetails) || []).map((h) => h.shortPhrase),
      notifications: (w.notifications || []).map((n) => `${n.type}: ${n.shortPhrase}`)
    }));
    const maxHazard = (data.summary && data.summary.hazards && data.summary.hazards.maxHazardIndex) || 0;
    const hazards = [...new Set(((data.summary && data.summary.hazards && data.summary.hazards.hazardDetails) || []).map((h) => h.shortPhrase))];
    const notes = points.flatMap((p) => (p.notifications || []).map((n) => `${p.km} km (+${p.eta} min): ${n}`));
    const coveredNote = covered < full.length ? ` Weather covers the first ${MAX_ETA_MINUTES} minutes of the trip (about ${wps[wps.length - 1].km} km).` : '';
    const text = `Weather along the route from ${labelOf(stops[0])} to ${labelOf(stops[stops.length - 1])} (${(total / 1000).toFixed(0)} km, ${formatDuration(duration * 60)}):${coveredNote}\n`
      + `${hazards.length ? `Hazards: ${hazards.join(', ')} (worst level ${maxHazard}).` : 'No weather hazards expected.'}\n`
      + (notes.length ? `Notifications:\n${notes.join('\n')}\n` : '')
      + `Conditions: ${points.filter((_, i) => i % Math.max(1, Math.floor(points.length / 8)) === 0).map((p) => `${p.km} km ${p.phrase || ''} ${show(p.temperature)}`).join(' | ')}\nA route weather timeline is shown when the client supports it.`;
    return ok(text, {
      kind: 'routeWeather', stops: stops.map((s) => ({ longitude: s.longitude, latitude: s.latitude, name: labelOf(s) })),
      route: { line: roundLine(simplifyLine(full, 400), 4), meters: best.meters, seconds: best.trafficSeconds ?? best.seconds },
      points, summary: { maxHazardIndex: maxHazard, hazards }, coveredMinutes: Math.min(MAX_ETA_MINUTES, Math.round(duration + delay)), leaveInMinutes: delay
    });
  }
}));

module.exports = { tools };
