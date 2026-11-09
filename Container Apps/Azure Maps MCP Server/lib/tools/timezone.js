'use strict';

const { VERSIONS } = require('../maps');
const { defineTool, ok, dataResult } = require('../define');
const { str, int, enumOf, places } = require('../schema');
const { InputError, intInRange, labelOf } = require('../args');

const parseOffset = (text) => {
  const m = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(String(text || '0:00'));
  if (!m) return 0;
  const minutes = Number(m[2] || 0) * 1440 + Number(m[3]) * 60 + Number(m[4]);
  return m[1] ? -minutes : minutes;
};

const formatOffset = (minutes) => {
  const sign = minutes < 0 ? '-' : '+';
  const a = Math.abs(minutes);
  return `${sign}${String(Math.floor(a / 60)).padStart(2, '0')}:${String(a % 60).padStart(2, '0')}`;
};

function zoneFrom(entry) {
  const ref = entry.ReferenceTime || {};
  const standard = parseOffset(ref.StandardOffset);
  const saving = parseOffset(ref.DaylightSavings);
  const now = new Date().toISOString();
  const next = (entry.TimeTransitions || []).find((t) => t.UtcStart > (ref.WallTime ? new Date(ref.WallTime).toISOString() : now));
  return {
    id: entry.Id,
    name: entry.Names && (saving ? entry.Names.Daylight : entry.Names.Standard) || entry.Id,
    genericName: entry.Names && entry.Names.Generic,
    abbreviation: ref.Tag,
    offsetMinutes: standard + saving,
    offset: formatOffset(standard + saving),
    daylightSaving: saving !== 0,
    wallTime: ref.WallTime,
    sunrise: ref.Sunrise,
    sunset: ref.Sunset,
    countries: (entry.Countries || []).map((c) => c.Code),
    ...(next ? { nextChange: { utc: next.UtcStart, abbreviation: next.Tag, offset: formatOffset(parseOffset(next.StandardOffset) + parseOffset(next.DaylightSavings)) } } : {})
  };
}

/** The UTC offset (like -08:00) and zone ID at a point and moment. Used to turn local times into ISO times. */
async function offsetAt(ctx, longitude, latitude, isoTimestamp) {
  const data = await ctx.maps.json('/timezone/byCoordinates/json', {
    version: VERSIONS.timezone, query: { query: [latitude, longitude], options: 'zoneInfo', timeStamp: isoTimestamp }
  });
  const entry = (data.TimeZones || [])[0];
  if (!entry) throw new InputError('No time zone was found for that location.');
  const z = zoneFrom(entry);
  return { offset: z.offset, id: z.id, name: z.name };
}

const tools = [];

tools.push(defineTool({
  name: 'get_timezone',
  title: 'Get time zones, local time and daylight hours',
  description: 'Get the current local time, UTC offset, daylight saving status, upcoming clock changes, and sunrise and sunset for one or more places, by address, coordinates or IANA zone ID. Shows a world clock with a meeting planner that finds the hours when everyone is in working hours.',
  properties: {
    locations: places('Places to get time zones for, as addresses or coordinates.', 1, 12),
    timezoneIds: { type: 'array', description: 'IANA time zone IDs, for example America/Los_Angeles or Europe/Paris. Use with or instead of locations.', items: { type: 'string' } },
    timeStamp: str('Reference time, ISO 8601. Default: now. Use it to see what time it will be on another date, including daylight saving changes.'),
    transitionsYears: int('Years of daylight saving changes to look ahead, 1 to 5. Default 1.', { minimum: 1, maximum: 5 }),
    workStart: str('Start of the working day for the meeting planner, HH:mm. Default 09:00.'),
    workEnd: str('End of the working day for the meeting planner, HH:mm. Default 17:00.')
  },
  widget: 'clock',
  async handler(args, ctx) {
    const targets = [];
    for (const [i, p] of (args.locations || []).entries()) targets.push({ kind: 'place', value: await ctx.places.resolve(p, `location ${i + 1}`) });
    for (const id of args.timezoneIds || []) targets.push({ kind: 'id', value: String(id) });
    if (!targets.length) throw new InputError('Provide locations or timezoneIds.');
    if (targets.length > 12) throw new InputError('Provide at most 12 places.');

    const zones = await Promise.all(targets.map(async (t) => {
      const query = {
        options: 'all', timeStamp: args.timeStamp, transitionsFrom: args.timeStamp, transitionsYears: intInRange(args.transitionsYears, 'transitionsYears', 1, 5, 1)
      };
      const data = t.kind === 'place'
        ? await ctx.maps.json('/timezone/byCoordinates/json', { version: VERSIONS.timezone, query: { ...query, query: [t.value.latitude, t.value.longitude] } })
        : await ctx.maps.json('/timezone/byId/json', { version: VERSIONS.timezone, query: { ...query, query: t.value } });
      const entry = (data.TimeZones || [])[0];
      if (!entry) throw new InputError(`No time zone was found for ${t.kind === 'place' ? labelOf(t.value) : t.value}.`);
      const z = zoneFrom(entry);
      return { ...z, label: t.kind === 'place' ? labelOf(t.value) : entry.Id.split('/').pop().replace(/_/g, ' '), ...(t.kind === 'place' ? { longitude: t.value.longitude, latitude: t.value.latitude } : {}) };
    }));

    const lines = zones.map((z) => {
      const time = z.wallTime ? z.wallTime.slice(11, 16) : '?';
      const sun = z.sunrise && z.sunset ? `, sunrise ${z.sunrise.slice(11, 16)}, sunset ${z.sunset.slice(11, 16)}` : '';
      return `${z.label}: ${time} ${z.abbreviation || ''} (UTC${z.offset}${z.daylightSaving ? ', daylight saving' : ''}), ${z.id}${sun}${z.nextChange ? `. Next clock change ${z.nextChange.utc.slice(0, 10)} to UTC${z.nextChange.offset}` : ''}`;
    });
    return ok(`Time zones:\n${lines.join('\n')}\nA world clock and meeting planner are shown when the client supports it.`, {
      kind: 'clock', zones, referenceUtc: args.timeStamp || new Date().toISOString(), workStart: args.workStart || '09:00', workEnd: args.workEnd || '17:00'
    });
  }
}));

const listCache = new Map();

tools.push(defineTool({
  name: 'list_timezones',
  title: 'List time zone IDs',
  description: 'List IANA or Windows time zone IDs, optionally filtered by text, for example "America" or "Pacific".',
  properties: { kind: enumOf('Which IDs to list. Default iana.', ['iana', 'windows']), filter: str('Text the ID or name must contain.'), limit: int('Maximum rows. Default 100.', { minimum: 1, maximum: 500 }) },
  async handler(args, ctx) {
    const kind = args.kind === 'windows' ? 'windows' : 'iana';
    if (!listCache.has(kind)) listCache.set(kind, await ctx.maps.json(`/timezone/enum${kind === 'windows' ? 'Windows' : 'Iana'}/json`, { version: VERSIONS.timezone }));
    const all = listCache.get(kind);
    const filter = (args.filter || '').toLowerCase();
    const rows = (filter ? all.filter((z) => JSON.stringify(z).toLowerCase().includes(filter)) : all).slice(0, args.limit || 100);
    return dataResult(`${rows.length} of ${all.length} ${kind === 'windows' ? 'Windows' : 'IANA'} time zone(s).`, rows);
  }
}));

tools.push(defineTool({
  name: 'get_iana_version',
  title: 'Get the IANA time zone database version',
  description: 'Get the version of the IANA time zone database that Azure Maps uses.',
  async handler(args, ctx) {
    const data = await ctx.maps.json('/timezone/ianaVersion/json', { version: VERSIONS.timezone });
    return ok(`IANA time zone database version ${data.Version}.`, data);
  }
}));

tools.push(defineTool({
  name: 'convert_windows_timezone',
  title: 'Convert a Windows time zone to IANA',
  description: 'Convert a Windows time zone ID such as "Pacific Standard Time" to its IANA IDs, optionally for one territory.',
  properties: { windowsId: str('The Windows time zone ID.'), territory: str('Optional territory code, for example US.') },
  required: ['windowsId'],
  async handler(args, ctx) {
    const data = await ctx.maps.json('/timezone/windowsToIana/json', { version: VERSIONS.timezone, query: { query: args.windowsId, territory: args.territory } });
    return dataResult(`IANA zones for "${args.windowsId}".`, data);
  }
}));

tools.push(defineTool({
  name: 'get_ip_location',
  title: 'Find the country of an IP address',
  description: 'Get the ISO country or region code for an IPv4 or IPv6 address.',
  properties: { ip: str('The IP address.') },
  required: ['ip'],
  async handler(args, ctx) {
    const data = await ctx.maps.json('/geolocation/ip/json', { version: VERSIONS.geolocation, query: { ip: args.ip } });
    const code = data.countryRegion && data.countryRegion.isoCode;
    return ok(`${data.ipAddress || args.ip} is in ${code || 'an unknown country or region'}.`, data);
  }
}));

module.exports = { tools, offsetAt, zoneFrom };
