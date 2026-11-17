import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
async function load(relative) {
  const source = readFileSync(new URL(relative, import.meta.url), 'utf8');
  return import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
}
const sandbox = { module: { exports: {} } };
vm.runInNewContext(readFileSync(new URL('../widgets/almanac/vendor/lunar.js', import.meta.url), 'utf8'), sandbox);
const { almanacFor } = await load('../widgets/almanac/model.js');
const newYear = almanacFor(new Date('2026-02-17T04:00:00Z'), sandbox.module.exports.Solar);
assert.equal(newYear.lunarDate, '正月初一');
assert.ok(newYear.festival.includes('春节'));
assert.ok(newYear.yi.length > 0 && newYear.ji.length > 0 && newYear.chong.length > 0 && newYear.term.length > 0);
assert.equal(almanacFor(new Date('2026-02-16T16:00:00Z'), sandbox.module.exports.Solar).key, '2026-02-17');
assert.equal(almanacFor(new Date('2026-02-16T15:59:59Z'), sandbox.module.exports.Solar).key, '2026-02-16');
const { condition, validateWeather, validLocation } = await load('../widgets/weather/model.js');
assert.equal(condition(0).label, '晴朗');
assert.equal(condition(95).scene, 'rain');
assert.equal(condition(75).scene, 'snow');
assert.equal(condition(null).label, '天气状况未知');
assert.throws(() => validateWeather({ current: { temperature_2m: null } }));
assert.throws(() => validateWeather(null));
assert.ok(validLocation({ name: '南京', latitude: 32.0617, longitude: 118.7778 }));
assert.ok(!validLocation({ name: 'x', latitude: 100, longitude: 118 }));
assert.ok(!validLocation({ name: 'x', latitude: '32', longitude: 118 }));
console.log('PASS: Chinese New Year, Beijing midnight, almanac fields, weather codes, invalid provider data and city coordinates');
