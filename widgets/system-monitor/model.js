export const METRICS = [{ key:'cpu', label:'CPU', color:'#7dd3fc', field:'cpuPercent' }, { key:'gpu', label:'GPU', color:'#b4a0fa', field:'gpuPercent' }, { key:'memory', label:'内存', color:'#f4c979', field:'memoryPercent' }];
export const LAYOUTS = ['bars','radar','rings','lines'];
export function normalizeConfig(value = {}) {
  return { layout:LAYOUTS.includes(value.layout)?value.layout:'bars', cpu:value.cpu!==false, gpu:value.gpu!==false, memory:value.memory!==false, paused:value.paused===true, background:value.background??{type:'none'} };
}
export const percent = value => Number.isFinite(value) && value >= 0 && value <= 100 ? value : null;
export const formatPercent = value => percent(value) === null ? '--' : `${Math.round(value)}%`;
export function effectiveLayout(config) { return config.layout==='radar' && METRICS.filter(metric=>config[metric.key]).length<3 ? 'bars' : config.layout; }
export function appendHistory(history, sample) {
  if (!Number.isFinite(sample.timestamp) || (history.length && sample.timestamp <= history.at(-1).timestamp)) return history;
  history.push(sample); if (history.length>60) history.shift(); return history;
}
