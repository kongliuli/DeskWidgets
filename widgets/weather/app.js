import { mountHost, loadManifest, setupSettings, readPreference, savePreference, send } from 'https://assets.widgets.local/runtime.js';
import { validLocation, validateWeather, condition } from './model.js';
const $ = id => document.getElementById(id);
const widget = document.querySelector('.widget');
mountHost(widget);
const manifest = await loadManifest();
setupSettings(widget, manifest.settings);
let location = readPreference('location', manifest.settings.location);
if (!validLocation(location)) location = manifest.settings.location;
send('widgetContext', { city: { name: location.name, lat: location.latitude, lon: location.longitude } });
let request;
function render(data, stale = false) {
  const { current, daily } = validateWeather(data);
  const weather = condition(current.weather_code);
  $('temperature').textContent = Math.round(current.temperature_2m);
  $('condition').textContent = weather.label;
  $('feels').textContent = `${Math.round(current.apparent_temperature)}°`;
  $('humidity').textContent = `${Math.round(current.relative_humidity_2m)}%`;
  $('wind').textContent = `${current.wind_speed_10m} km/h`;
  const high = daily?.temperature_2m_max?.[0], low = daily?.temperature_2m_min?.[0];
  $('range').textContent = Number.isFinite(high) && Number.isFinite(low) ? `最高 ${Math.round(high)}° / 最低 ${Math.round(low)}°` : '暂无当天最高 / 最低温';
  $('status').textContent = `${stale ? '缓存' : '更新'} ${current.time.slice(11, 16)}`;
  widget.dataset.condition = weather.scene;
  widget.dataset.night = String(current.is_day === 0);
  widget.dataset.ready = 'true';
  delete widget.dataset.networkError;
}
async function update(force = false) {
  request?.abort();
  const controller = new AbortController(); request = controller;
  const place = { ...location };
  $('city').firstChild.textContent = `${place.name} `;
  const cache = readPreference('weather-cache', null);
  let cached = false;
  if (cache?.latitude === place.latitude && cache?.longitude === place.longitude) {
    try { render(cache.data, true); cached = true; } catch { }
    if (cached && !force && Date.now() - cache.saved < 900000) return;
  }
  if (!cached) {
    for (const id of ['temperature', 'feels', 'humidity', 'wind']) $(id).textContent = '--';
    $('condition').textContent = '正在获取天气'; $('range').textContent = '等待在线数据';
  }
  $('status').textContent = '连接中…';
  const timeout = setTimeout(() => controller.abort(), 12000);
  try {
    const params = new URLSearchParams({ latitude: place.latitude, longitude: place.longitude, current: 'temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m', daily: 'temperature_2m_max,temperature_2m_min', timezone: 'auto', forecast_days: '1' });
    const response = await fetch(`https://api.open-meteo.com/v1/forecast?${params}`, { signal: controller.signal });
    if (!response.ok) throw new Error(`天气服务 HTTP ${response.status}`);
    const data = validateWeather(await response.json());
    if (request !== controller) return;
    render(data); savePreference('weather-cache', { data, saved: Date.now(), latitude: place.latitude, longitude: place.longitude });
  } catch (error) {
    if (request !== controller) return;
    $('status').textContent = cached ? '离线 · 显示缓存' : '获取失败 · 点击刷新';
    if (!cached) { $('temperature').textContent = '--'; $('condition').textContent = '暂时无法连接天气服务'; $('range').textContent = '检查网络后重试'; }
    widget.dataset.ready = 'true'; widget.dataset.networkError = 'true';
  } finally { clearTimeout(timeout); }
}
$('refresh').addEventListener('click', () => update(true));
const dialog = $('city-dialog');
$('city').addEventListener('click', () => { send('setInteractive', { enabled: true }); dialog.showModal(); setTimeout(() => dialog.querySelector('input').focus(), 150); });
$('cancel-city').addEventListener('click', () => dialog.close());
dialog.addEventListener('close', () => send('setInteractive', { enabled: false }));
let searching;
$('city-search').addEventListener('submit', async event => {
  event.preventDefault(); searching?.abort(); searching = new AbortController();
  const controller = searching;
  const timeout = setTimeout(() => controller.abort(), 10000);
  $('search-status').textContent = '正在搜索…'; $('city-results').replaceChildren();
  try {
    const params = new URLSearchParams({ name: new FormData(event.target).get('query').trim(), count: '6', language: 'zh', format: 'json' });
    const response = await fetch(`https://geocoding-api.open-meteo.com/v1/search?${params}`, { signal: controller.signal });
    if (!response.ok) throw new Error('城市搜索服务暂不可用');
    const data = await response.json();
    if (controller !== searching) return;
    const results = (data.results ?? []).filter(validLocation);
    $('search-status').textContent = results.length ? '选择需要的城市' : '没有找到城市，可尝试英文名';
    for (const place of results) {
      const button = document.createElement('button');
      button.textContent = [place.name, place.admin1, place.country].filter(Boolean).join(' · ');
      button.addEventListener('click', () => {
        location = { name: place.name, latitude: place.latitude, longitude: place.longitude };
        if (!savePreference('location', location)) { $('search-status').textContent = '无法保存城市设置'; return; }
        send('widgetContext', { city: { name: location.name, lat: location.latitude, lon: location.longitude } });
        dialog.close(); $('temperature').textContent = '--'; update(true);
      });
      $('city-results').append(button);
    }
  } catch { if (controller === searching) $('search-status').textContent = '搜索失败，请检查网络后重试'; }
  finally { clearTimeout(timeout); }
});
update(); setInterval(() => update(true), 900000);
