import { mountHost,setupSettings,loadManifest,send,savePreference } from 'https://assets.widgets.local/runtime.js';
import { METRICS,LAYOUTS,normalizeConfig,effectiveLayout,formatPercent,appendHistory,percent } from './model.js';
import { drawChart } from './charts.js';
const widget=document.querySelector('.widget'),$=id=>document.getElementById(id);
mountHost(widget);
let config,sample=null,history=[],drawPending=0;
const manifest=await loadManifest();
function subscribe(){send('subscribeSystemMetrics',{channels:document.hidden||config.paused?[]:METRICS.filter(metric=>config[metric.key]).map(metric=>metric.key)});}
function configure(value){config=normalizeConfig(value);subscribe();render();}
const fields=`<label>图表布局<select name="layout" data-setting><option value="bars">进度条</option><option value="radar">雷达图（3 项）</option><option value="rings">独立环形图</option><option value="lines">两分钟折线图</option></select></label><div class="metric-switches"><label><input type="checkbox" name="cpu" data-setting>CPU</label><label><input type="checkbox" name="gpu" data-setting>GPU</label><label><input type="checkbox" name="memory" data-setting>内存</label><label><input type="checkbox" name="paused" data-setting>暂停采样</label></div>`;
config=normalizeConfig(setupSettings(widget,manifest.settings,fields,configure,data=>{const layout=data.get('layout');if(!LAYOUTS.includes(layout))throw new Error('未知图表布局');return{layout,cpu:data.has('cpu'),gpu:data.has('gpu'),memory:data.has('memory'),paused:data.has('paused')};}));
function save(){if(!savePreference('settings',config)){$('sampling-state').textContent='设置保存失败';return;}configure(config);}
function render(){
  const active=METRICS.filter(metric=>config[metric.key]),layout=effectiveLayout(config),wide=widget.clientWidth>=620&&widget.clientHeight>=420;
  widget.dataset.dashboard=String(wide);$('dashboard').hidden=!wide||!active.length;$('layouts').hidden=wide;widget.querySelector('.plot').hidden=wide&&active.length>0;
  for(const button of $('layouts').children)button.setAttribute('aria-pressed',String(button.dataset.layout===config.layout));
  $('sampling-state').textContent=!active.length?'未启用指标':config.paused?'已暂停':sample?'每 2 秒更新':'数据预热';$('pause').textContent=config.paused?'恢复':'暂停';
  $('bars').hidden=layout!=='bars'||!active.length;$('chart').hidden=layout==='bars'||!active.length;$('empty').hidden=active.length>0;
  $('bars').replaceChildren();$('legend').replaceChildren();
  for(const metric of active){const value=percent(sample?.[metric.field]),row=document.createElement('div');row.className='bar-row';row.style.setProperty('--metric-color',metric.color);row.innerHTML=`<div class="bar-label"><span>${metric.label}</span><span>${formatPercent(value)}</span></div><progress max="100" aria-label="${metric.label}"></progress>`;const progress=row.querySelector('progress');progress.value=value??0;progress.setAttribute('aria-valuetext',value===null?'不可用':`${Math.round(value)}%`);progress.style.opacity=value===null?'.3':'1';$('bars').append(row);const legend=document.createElement('span');legend.style.setProperty('--metric-color',metric.color);legend.innerHTML=`<i></i>${metric.label} ${formatPercent(value)}`;$('legend').append(legend);}
  $('dashboard-bars').replaceChildren(...[...$('bars').children].map(row=>row.cloneNode(true)));$('radar-hint').hidden=active.length===3;$('radar-chart').hidden=active.length!==3;
  $('legend').hidden=(!wide&&layout==='bars')||!active.length;
  const detail=[];if(config.paused)detail.push('已暂停');if(sample?.totalMemoryBytes&&config.memory)detail.push(`${(sample.usedMemoryBytes/1073741824).toFixed(1)} / ${(sample.totalMemoryBytes/1073741824).toFixed(1)} GiB`);if(config.gpu)detail.push(sample?.gpuPercent===null?`GPU ${sample.gpuStatus}`:'GPU 最忙引擎');if(config.layout==='radar'&&layout!=='radar')detail.unshift('雷达需 3 项，当前为条形');$('detail').textContent=detail.join(' · ')||'整机负载，2 秒采样';
  $('chart').setAttribute('aria-label',active.map(metric=>`${metric.label} ${formatPercent(sample?.[metric.field])}`).join('，'));
  cancelAnimationFrame(drawPending);drawPending=requestAnimationFrame(()=>{drawPending=0;if(wide){for(const mode of ['radar','rings','lines'])if(mode!=='radar'||active.length===3)drawChart($(mode+'-chart'),mode,active,sample,history);}else drawChart($('chart'),layout,active,sample,history);});
  widget.dataset.layout=layout;widget.dataset.ready='true';
}
$('layouts').addEventListener('click',event=>{const button=event.target.closest('[data-layout]');if(button){config.layout=button.dataset.layout;save();}});
$('pause').addEventListener('click',()=>{config.paused=!config.paused;save();});
window.chrome?.webview?.addEventListener('message',({data})=>{if(data.type!=='systemMetrics'||config.paused)return;sample=data.sample;appendHistory(history,sample);render();});
document.addEventListener('visibilitychange',subscribe);
window.addEventListener('pagehide',()=>{send('subscribeSystemMetrics',{channels:[]});cancelAnimationFrame(drawPending);observer.disconnect();},{once:true});
const observer=new ResizeObserver(()=>render());observer.observe(widget);window.addEventListener('resize',render);
subscribe();render();
