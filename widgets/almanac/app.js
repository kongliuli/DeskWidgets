import { mountHost, loadManifest, setupSettings } from 'https://assets.widgets.local/runtime.js';
import { almanacFor } from './model.js';
const widget = document.querySelector('.widget');
mountHost(widget);
try {
  const manifest = await loadManifest();
  setupSettings(widget, manifest.settings);
  let previous;
  function render() {
    const data = almanacFor(new Date(), window.Solar);
    if (data.key === previous) return;
    previous = data.key;
    const fields = { month: 'month', weekday: 'weekday', day: 'day', 'lunar-date': 'lunarDate', year: 'year', festival: 'festival', ganzhi: 'ganzhi', term: 'term', chong: 'chong', god: 'god', wealth: 'wealth', pengzu: 'pengzu' };
    for (const [id, key] of Object.entries(fields)) document.getElementById(id).textContent = data[key];
    document.getElementById('yi').textContent = data.yi.join('、');
    document.getElementById('ji').textContent = data.ji.join('、');
    widget.dataset.ready = 'true';
  }
  render(); setInterval(render, 30000);
} catch (error) { document.getElementById('lunar-date').textContent = error.message; }
