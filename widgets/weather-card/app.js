import { WeatherEngine, SCENES } from './weather-engine.js';
import { mountAppearanceControls } from 'https://assets.widgets.local/appearance.js';
import { bindWidgetDrag, applyWidgetLock } from 'https://assets.widgets.local/drag.js';

export const DEFAULT_CITY = { name: '南京', lat: 32.0603, lon: 118.7969 };
export const PREVIEW_NAMES = { clear: '晴天', 'partly-cloudy': '多云', overcast: '阴天', rain: '暴雨', thunderstorm: '雷阵雨', snow: '大雪', fog: '大雾' };
const CODES = new Map([
  [0, ['clear', .1, '晴朗']], [1, ['partly-cloudy', .25, '晴间多云']], [2, ['partly-cloudy', .6, '多云']], [3, ['overcast', 1, '阴天']],
  [45, ['fog', .65, '雾']], [48, ['fog', .9, '冻雾']],
  [51, ['rain', .15, '毛毛雨']], [53, ['rain', .3, '毛毛雨']], [55, ['rain', .5, '强毛毛雨']], [56, ['rain', .35, '冻毛毛雨']], [57, ['rain', .65, '冻毛毛雨']],
  [61, ['rain', .3, '小雨']], [63, ['rain', .65, '中雨']], [65, ['rain', 1, '大雨']], [66, ['rain', .5, '冻雨']], [67, ['rain', .85, '强冻雨']],
  [71, ['snow', .25, '小雪']], [73, ['snow', .6, '中雪']], [75, ['snow', 1, '大雪']], [77, ['snow', .35, '雪粒']],
  [80, ['rain', .4, '阵雨']], [81, ['rain', .7, '强阵雨']], [82, ['rain', 1, '暴雨']], [85, ['snow', .45, '阵雪']], [86, ['snow', .9, '强阵雪']],
  [95, ['thunderstorm', .75, '雷阵雨']], [96, ['thunderstorm', .9, '雷雨伴冰雹']], [99, ['thunderstorm', 1, '强雷雨伴冰雹']]
]);
export function normalizeWeather(code, current = {}) {
  const [scene, intensity, label] = CODES.get(code) ?? ['overcast', .5, '天气状况未知'];
  return { scene, intensity, label, isDay: current.is_day === 1, windSpeed: Number.isFinite(current.wind_speed_10m) ? current.wind_speed_10m : 0, windDirection: Number.isFinite(current.wind_direction_10m) ? current.wind_direction_10m : 270 };
}
export function validCity(city) {
  return Boolean(city && typeof city.name === 'string' && city.name.trim().length > 0 && city.name.length <= 100 && Number.isFinite(city.lat) && Math.abs(city.lat) <= 90 && Number.isFinite(city.lon) && Math.abs(city.lon) <= 180);
}
export function weatherUrl(city, days = 1) {
  if (!validCity(city)) throw new TypeError('无效的城市坐标');
  const params = new URLSearchParams({ latitude: city.lat, longitude: city.lon,
    current: 'temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,cloud_cover,wind_speed_10m,wind_direction_10m',
    hourly: 'temperature_2m,weather_code,is_day', forecast_days: String(days), timezone: 'auto',
    // Absolute timestamps keep hourly forecasts unambiguous across time zones and DST.
    timeformat: 'unixtime' });
  return `https://api.open-meteo.com/v1/forecast?${params}`;
}
export function validateWeather(data) {
  const current = data?.current;
  const keys = ['time', 'temperature_2m', 'relative_humidity_2m', 'apparent_temperature', 'is_day', 'precipitation', 'weather_code', 'cloud_cover', 'wind_speed_10m', 'wind_direction_10m'];
  if (!current || !keys.every(key => Number.isFinite(current[key])) || !Number.isInteger(current.weather_code) || ![0, 1].includes(current.is_day) || current.relative_humidity_2m < 0 || current.relative_humidity_2m > 100 || current.cloud_cover < 0 || current.cloud_cover > 100 || current.wind_speed_10m < 0) throw new Error('天气服务返回了不完整或无效的数据');
  const hourly = data.hourly;
  if (!hourly || !['time', 'temperature_2m', 'weather_code', 'is_day'].every(key => Array.isArray(hourly[key]) && hourly[key].length === hourly.time.length)) throw new Error('逐小时预报数据不完整');
  try { if (typeof data.timezone !== 'string' || !data.timezone) throw new Error(); new Intl.DateTimeFormat('zh-CN', { timeZone: data.timezone }).format(); } catch { throw new Error('天气服务返回了无效的时区'); }
  return data;
}
export function futureHours(data, now = Date.now()) {
  const hourly = data.hourly, result = [];
  for (let i = 0; i < hourly.time.length && result.length < 5; i++) {
    const time = hourly.time[i] * 1000;
    if (time <= now || !Number.isFinite(time) || !Number.isFinite(hourly.temperature_2m[i]) || !Number.isInteger(hourly.weather_code[i]) || ![0, 1].includes(hourly.is_day[i])) continue;
    result.push({ time, temperature: hourly.temperature_2m[i], ...normalizeWeather(hourly.weather_code[i], { is_day: hourly.is_day[i] }) });
  }
  return result;
}
export function iconMarkup(scene, isDay = true) {
  const sun = '<circle cx="12" cy="10" r="3.5"/><path d="M12 2v2m0 12v2M4 10H2m20 0h-2M5 3l1.5 1.5M19 3l-1.5 1.5"/>';
  const moon = '<path d="M15 3a7 7 0 1 0 6 10 6 6 0 0 1-6-10Z"/>';
  const cloud = '<path d="M5 17a4 4 0 0 1 0-8 5 5 0 0 1 9-2 4 4 0 1 1 4 10Z"/>';
  const detail = ({ rain: '<path d="m7 19-1 2m6-2-1 2m6-2-1 2"/>', thunderstorm: '<path d="m12 16-3 4h4l-2 3"/>', snow: '<path d="M7 19v3m-1-1.5h2m6-1.5v3m-1-1.5h2"/>', fog: '<path d="M3 19h18M5 22h14"/>' })[scene] ?? '';
  const body = scene === 'clear' ? (isDay ? sun : moon) : scene === 'partly-cloudy' ? `<g opacity=".6" transform="translate(3 -2) scale(.65)">${isDay ? sun : moon}</g>${cloud}` : cloud + detail;
  return `<svg viewBox="0 0 24 24" aria-hidden="true">${body}</svg>`;
}

export let engine;
if (typeof document !== 'undefined') startApp().catch(error => {
  document.getElementById('description').textContent = '天气组件初始化失败';
  document.getElementById('status').textContent = error.message;
});

async function startApp() {
  const $ = id => document.getElementById(id), card = document.querySelector('.weather-card');
  const send = (type, fields = {}) => window.chrome?.webview?.postMessage({ type, ...fields });
  const read = (key, fallback) => { try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; } };
  const save = (key, value) => { try { localStorage.setItem(key, JSON.stringify(value)); return true; } catch { return false; } };
  let city = read('weather-card.city', DEFAULT_CITY);
  if (!validCity(city)) city = DEFAULT_CITY;
  send('widgetContext', { city });
  let liveData = null, simulation = null, status = '连接中…', request = null, searchRequest = null, lastUpdate = 0;
  let rendererState = '';
  let hourlySignature = '';
  let forecastTimer = 0;
  engine = new WeatherEngine($('atmosphere'), card);
  card.addEventListener('appearancechange', ({ detail }) => engine.setEnabled(detail.transparency < 100));
  const simulator = $('simulator'), cityDialog = $('city-dialog'), appearanceDialog = $('appearance-dialog');
  const appearance = mountAppearanceControls(card, $('appearance-form'));
  const interactive = () => send('setInteractive', { enabled: cityDialog.open || appearanceDialog.open || !simulator.hidden });
  $('appearance-toggle').addEventListener('click', () => { appearance.reset(); appearanceDialog.querySelector('.appearance-error').textContent = ''; appearanceDialog.showModal(); interactive(); setTimeout(() => appearanceDialog.querySelector('input').focus(), 150); });
  $('cancel-appearance').addEventListener('click', () => appearanceDialog.close());
  appearanceDialog.addEventListener('close', interactive);
  $('appearance-form').addEventListener('submit', event => {
    event.preventDefault();
    try { appearance.save(); appearanceDialog.close(); } catch (error) { appearanceDialog.querySelector('.appearance-error').textContent = error.message; }
  });
  function render() {
    $('city').firstChild.textContent = `${city.name} `;
    const current = liveData?.current;
    const state = simulation ?? normalizeWeather(current?.weather_code, current ?? { is_day: 1 });
    const nextKey = JSON.stringify(state);
    if (rendererState !== nextKey) { engine.setWeather(state); rendererState = nextKey; }
    $('temperature').textContent = current ? Math.round(current.temperature_2m) : '--';
    $('description').textContent = simulation ? PREVIEW_NAMES[state.scene] : current ? state.label : '正在获取天气';
    $('feels').textContent = `体感 ${current ? Math.round(current.apparent_temperature) : '--'}°`;
    $('humidity').textContent = `湿度 ${current ? Math.round(current.relative_humidity_2m) : '--'}%`;
    $('wind').textContent = `${simulation ? '预览风速' : '风速'} ${current || simulation ? Math.round(state.windSpeed) : '--'} km/h`;
    $('status').textContent = status;
    $('mode').textContent = simulation ? '效果预览' : '实时';
    $('mode').classList.toggle('preview', Boolean(simulation));
    const hours = liveData ? futureHours(liveData) : [];
    const signature = JSON.stringify([liveData?.timezone, hours.map(hour => [hour.time, hour.temperature, hour.scene, hour.isDay])]);
    if (signature !== hourlySignature) {
      hourlySignature = signature;
      clearTimeout(forecastTimer); forecastTimer = 0;
      $('hourly').replaceChildren();
      const formatter = new Intl.DateTimeFormat('zh-CN', { timeZone: liveData?.timezone ?? 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
      for (let i = 0; i < 5; i++) {
        const item = document.createElement('li'), hour = hours[i];
        if (hour) {
          const time = document.createElement('time'); time.dateTime = new Date(hour.time).toISOString(); time.textContent = formatter.format(hour.time);
          item.append(time); item.insertAdjacentHTML('beforeend', iconMarkup(hour.scene, hour.isDay));
          const temperature = document.createElement('span'); temperature.textContent = `${Math.round(hour.temperature)}°`; item.append(temperature);
          item.setAttribute('aria-label', `${time.textContent} ${hour.label} ${temperature.textContent}`);
        } else item.innerHTML = '<time>--:--</time><span aria-hidden="true">·</span><span>--°</span>';
        $('hourly').append(item);
      }
    }
    if (!document.hidden && !forecastTimer && hours[0]) {
      forecastTimer = setTimeout(() => {
        forecastTimer = 0;
        if (document.hidden) return;
        render();
        if (liveData && futureHours(liveData).length < 5) refresh(true);
      }, Math.max(30, hours[0].time - Date.now() + 30));
    }
    $('day-mode').textContent = state.isDay ? '白天' : '夜晚';
    $('day-mode').setAttribute('aria-pressed', String(state.isDay));
    $('wind-speed').value = Math.min(100, Math.round(state.windSpeed)); $('wind-value').value = $('wind-speed').value;
    for (const button of $('scene-options').children) button.setAttribute('aria-pressed', String(button.dataset.scene === state.scene));
    card.dataset.ready = 'true';
  }

  function beginPreview(scene) {
    simulation = { ...(simulation ?? normalizeWeather(liveData?.current.weather_code, liveData?.current ?? { is_day: 1 })), scene, intensity: ['rain', 'thunderstorm', 'snow', 'fog'].includes(scene) ? 1 : .6 };
    render();
  }
  for (const scene of SCENES) {
    const button = document.createElement('button'); button.type = 'button'; button.dataset.scene = scene; button.textContent = PREVIEW_NAMES[scene]; button.setAttribute('aria-pressed', 'false');
    button.addEventListener('click', () => beginPreview(scene)); $('scene-options').append(button);
  }
  $('preview-toggle').addEventListener('click', () => {
    simulator.hidden = !simulator.hidden; $('preview-toggle').setAttribute('aria-expanded', String(!simulator.hidden)); interactive();
  });
  $('day-mode').addEventListener('click', () => {
    if (!simulation) beginPreview(normalizeWeather(liveData?.current.weather_code).scene);
    simulation.isDay = !simulation.isDay; render();
  });
  $('wind-speed').addEventListener('input', event => {
    const speed = Number(event.target.value);
    if (!simulation) beginPreview(normalizeWeather(liveData?.current.weather_code).scene);
    simulation.windSpeed = speed; render();
  });
  $('restore-live').addEventListener('click', () => {
    simulation = null; simulator.hidden = true; $('preview-toggle').setAttribute('aria-expanded', 'false'); interactive(); render();
    if (!liveData) refresh(true);
  });
  $('close-widget').addEventListener('click', () => send('closeWidget'));
  $('lock-widget').addEventListener('click', () => send('toggleLock'));
  bindWidgetDrag(card, send);
  window.chrome?.webview?.addEventListener('message', ({ data }) => { if (data.type === 'hostState') applyWidgetLock(card, data.locked); });

  async function fetchWeather(place, days, controller) {
    const response = await fetch(weatherUrl(place, days), { signal: controller.signal });
    if (!response.ok) throw new Error(`天气服务 HTTP ${response.status}`);
    return validateWeather(await response.json());
  }
  async function refresh(force = false) {
    request?.abort(); const controller = new AbortController(); request = controller;
    const place = { ...city }, cache = read('weather-card.cache', null);
    if (cache?.city?.lat === place.lat && cache?.city?.lon === place.lon) {
      try {
        liveData = validateWeather(cache.data); status = '缓存数据'; render();
        if (!force && Number.isFinite(cache.saved) && Date.now() - cache.saved >= 0 && Date.now() - cache.saved < 900000 && futureHours(liveData).length === 5) { lastUpdate = cache.saved; return; }
      } catch { liveData = null; }
    }
    status = '正在更新…'; render();
    const timeout = setTimeout(() => controller.abort(), 14000);
    try {
      let data = await fetchWeather(place, 1, controller);
      if (futureHours(data).length < 5) {
        try { data = await fetchWeather(place, 2, controller); } catch { /* Keep valid current data if the supplemental overnight forecast fails. */ }
      }
      if (request !== controller) return;
      liveData = data; lastUpdate = Date.now();
      const formatter = new Intl.DateTimeFormat('zh-CN', { timeZone: data.timezone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
      status = `更新 ${formatter.format(data.current.time * 1000)}`;
      if (futureHours(data).length < 5) status += ' · 预报不完整';
      save('weather-card.cache', { city: place, data, saved: lastUpdate }); render();
    } catch {
      if (request !== controller) return;
      if (liveData) {
        const stamp = new Intl.DateTimeFormat('zh-CN', { timeZone: liveData.timezone, month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(liveData.current.time * 1000);
        status = `离线缓存 ${stamp}`;
      } else status = '获取失败 · 点击重试';
      render();
      if (!liveData && !simulation) $('description').textContent = '暂时无法连接天气服务';
    } finally { clearTimeout(timeout); }
  }
  $('refresh').addEventListener('click', () => refresh(true));
  $('city').addEventListener('click', () => { cityDialog.showModal(); interactive(); setTimeout(() => cityDialog.querySelector('input').focus(), 150); });
  $('cancel-search').addEventListener('click', () => cityDialog.close());
  cityDialog.addEventListener('close', () => { searchRequest?.abort(); interactive(); });
  $('city-search').addEventListener('submit', async event => {
    event.preventDefault(); searchRequest?.abort(); const controller = new AbortController(); searchRequest = controller;
    const timeout = setTimeout(() => controller.abort(), 10000);
    $('search-status').textContent = '搜索中…'; $('search-results').replaceChildren();
    try {
      const query = new FormData(event.target).get('query').trim();
      const params = new URLSearchParams({ name: query, count: '5', language: 'zh' });
      const response = await fetch(`https://geocoding-api.open-meteo.com/v1/search?${params}`, { signal: controller.signal });
      if (!response.ok) throw new Error('城市检索暂不可用');
      const data = await response.json(); if (searchRequest !== controller) return;
      const results = (Array.isArray(data.results) ? data.results : []).filter(item => validCity({ name: item.name, lat: item.latitude, lon: item.longitude })).slice(0, 5);
      $('search-status').textContent = results.length ? '选择城市' : '没有找到城市，可尝试英文名';
      for (const item of results) {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = [item.name, item.admin1, item.country].filter(Boolean).join(' · ');
        button.addEventListener('click', () => {
          const chosen = { name: item.name, lat: item.latitude, lon: item.longitude };
          if (!save('weather-card.city', chosen)) { $('search-status').textContent = '无法保存城市，请检查浏览器配置目录'; return; }
          city = chosen; send('widgetContext', { city }); liveData = null; simulation = null; cityDialog.close(); refresh(true);
        });
        $('search-results').append(button);
      }
    } catch { if (searchRequest === controller && cityDialog.open) $('search-status').textContent = '搜索失败，请检查网络后重试'; }
    finally { clearTimeout(timeout); }
  });
  const interval = setInterval(() => { if (!document.hidden) refresh(true); }, 900000);
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { clearTimeout(forecastTimer); forecastTimer = 0; return; }
    render();
    if (Date.now() - lastUpdate >= 900000 || (liveData && futureHours(liveData).length < 5)) refresh(true);
  });
  window.addEventListener('pagehide', () => { request?.abort(); searchRequest?.abort(); clearInterval(interval); clearTimeout(forecastTimer); engine.destroy(); }, { once: true });
  render(); refresh();
}
