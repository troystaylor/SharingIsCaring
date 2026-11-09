'use strict';

// Small JSON Schema builders shared by the tool modules.

const str = (description, extra = {}) => ({ type: 'string', description, ...extra });
const num = (description, extra = {}) => ({ type: 'number', description, ...extra });
const int = (description, extra = {}) => ({ type: 'integer', description, ...extra });
const bool = (description) => ({ type: 'boolean', description });
const enumOf = (description, values) => ({ type: 'string', description, enum: values });
const arrayOf = (description, items) => ({ type: 'array', description, items });
const strings = (description) => arrayOf(description, { type: 'string' });

/**
 * A location the caller can give either as text (an address or place name that the server looks up) or as
 * coordinates. This saves the agent a separate geocoding call for every point.
 */
const placeProperties = () => ({
  query: str('Address or place name, for example "Space Needle, Seattle". Used when longitude and latitude are not given.'),
  longitude: num('Longitude, -180 to 180.'),
  latitude: num('Latitude, -90 to 90.'),
  name: str('Optional label to show for this point.')
});

const place = (description) => ({
  type: 'object',
  description: `${description} Give either query (an address or place name, which the server looks up) or both longitude and latitude.`,
  properties: placeProperties()
});

const places = (description, min, max) => ({
  type: 'array',
  description: `${description} Each item is a location: give query (text) or longitude and latitude.`,
  items: { type: 'object', properties: placeProperties() },
  ...(min ? { minItems: min } : {}),
  ...(max ? { maxItems: max } : {})
});

module.exports = { str, num, int, bool, enumOf, arrayOf, strings, place, places };
