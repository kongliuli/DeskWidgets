import { mountHost, loadManifest, setupSettings, applyBackground, savePreference, send } from './runtime.js';
import { clockParts } from './time.js';
export async function runClock() {
  const widget = document.querySelector('.widget');
  mountHost(widget);
  try {
    const manifest = await loadManifest();
    const zones = [['UTC', 'UTC 标准时间'], ['Asia/Shanghai', '北京'], ['America/New_York', '纽约 · 美东'], ['America/Los_Angeles', '洛杉矶 · 美西']];
    const markup = `<label>地区<select name="timeZone" data-setting>${zones.map(([zone, city]) => `<option value="${zone}">${city}</option>`).join('')}</select></label>`;
    const picker = document.createElement('select'); picker.className = 'zone-picker'; picker.setAttribute('aria-label', '选择时钟地区');
    for (const [value, label] of zones) { const option = document.createElement('option'); option.value = value; option.textContent = label; picker.append(option); }
    const cityLabel = document.createElement('span'); cityLabel.className = 'city-label';
    document.querySelector('.city').replaceChildren(picker, cityLabel);
    if(widget.classList.contains('locked')){picker.disabled=true;picker.dataset.lockDisabled='false';}
    picker.addEventListener('pointerdown', () => send('setInteractive', { enabled: true }));
    picker.addEventListener('blur', () => send('setInteractive', { enabled: Boolean(widget.querySelector('dialog[open]')) }));
    let timeZone, currentSettings;
    const updateText = (selector, value) => {
      const node = document.querySelector(selector);
      if (node.textContent !== value) node.textContent = value;
    };
    function configure(settings) {
      const zone = zones.find(([zone]) => zone === settings.timeZone) ?? zones[0];
      currentSettings = settings; picker.value = zone[0]; cityLabel.textContent = zone[1];
      timeZone = zone[0];
      send('widgetContext', { timeZone });
      if (settings.background?.type === 'scene') applyBackground(widget, { type: 'scene', scene: ({ UTC: 'utc', 'Asia/Shanghai': 'beijing', 'America/New_York': 'new-york', 'America/Los_Angeles': 'los-angeles' })[timeZone] });
      render();
    }
    function render() {
      const p = clockParts(new Date(), timeZone);
      updateText('.main-time', p.time.slice(0, 5));
      updateText('.seconds', p.time.slice(5));
      updateText('.date', p.date);
      updateText('.zone', p.zone);
      if (widget.dataset.ready !== 'true') widget.dataset.ready = 'true';
    }
    const settings = setupSettings(widget, manifest.settings, markup, configure, data => {
      if (!zones.some(([zone]) => zone === data.get('timeZone'))) throw new Error('不支持的时区');
      return { timeZone: data.get('timeZone') };
    });
    picker.addEventListener('change', () => {
      const next = { ...currentSettings, timeZone: picker.value };
      if (!savePreference('settings', next)) { picker.value = timeZone; updateText('.zone', '保存失败'); return; }
      configure(next); send('setInteractive', { enabled: false });
    });
    widget.querySelector('[data-action=settings]').addEventListener('click', () => { widget.querySelector('.widget-settings form').elements.timeZone.value = timeZone; });
    configure(settings);
    function tick() { render(); setTimeout(tick, 1000 - Date.now() % 1000); }
    tick();
  } catch (error) { document.querySelector('.city').textContent = error.message; }
}
