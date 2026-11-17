import {mountHost,setupSettings,loadManifest,send} from 'https://assets.widgets.local/runtime.js';
import {normalizeConfig,formatDbfs,peakToDbfs} from './model.js';
const widget=document.querySelector('.widget'),$=id=>document.getElementById(id);
mountHost(widget);
let config,sample=null,interacting=false,timer=0,lastSent=0,errorUntil=0,errorMessage='';
const manifest=await loadManifest();
function subscribe(){send('subscribeAudio',{enabled:!document.hidden,meter:config.meter});}
function configure(value){config=normalizeConfig(value);subscribe();render();}
config=normalizeConfig(setupSettings(widget,manifest.settings,'<label style="display:flex;align-items:center;gap:6px"><input style="width:auto" type="checkbox" name="meter" data-setting>显示输出电平（10 Hz）</label>',configure,data=>({meter:data.has('meter')})));
function render(){
  const available=Number.isFinite(sample?.volume);
  if(!interacting){$('volume').textContent=available?Math.round(sample.volume):'--';$('volume-slider').value=available?Math.round(sample.volume):0;}
  $('volume-slider').disabled=!available||widget.classList.contains('locked');$('mute').disabled=!available||widget.classList.contains('locked');
  $('muted-state').textContent=sample?.muted?'已静音':'';$('mute').textContent=sample?.muted?'取消静音':'静音';$('meter-panel').hidden=!config.meter;
  $('decibels').textContent=formatDbfs(sample?.peak);
  const db=peakToDbfs(sample?.peak),level=Number.isFinite(sample?.peak)?Math.max(0,Math.min(100,((db??-80)+80)/80*100)):0;
  $('level').style.width=`${level}%`;$('level').style.background=db>-6?'#f29d91':db>-18?'#e3cf8a':'#7ccca9';$('level-meter').setAttribute('aria-valuenow',String(Math.max(-80,db??-80)));$('level-meter').setAttribute('aria-valuetext',formatDbfs(sample?.peak));
  $('audio-status').textContent=Date.now()<errorUntil?errorMessage:sample?.status??'数据预热';
  $('level-meter').title='数字峰值，位于系统主音量衰减之前；不是扬声器声压 dB SPL。独占模式下的软件电平计可能不可用。';
  widget.dataset.ready='true';
}
function sendVolume(){lastSent=performance.now();send('setAudioVolume',{value:Number($('volume-slider').value)});}
$('volume-slider').addEventListener('pointerdown',()=>{interacting=true;send('setInteractive',{enabled:true});});
$('volume-slider').addEventListener('input',()=>{$('volume').textContent=$('volume-slider').value;clearTimeout(timer);const delay=Math.max(0,80-(performance.now()-lastSent));timer=setTimeout(sendVolume,delay);});
$('volume-slider').addEventListener('change',()=>{clearTimeout(timer);sendVolume();interacting=false;});
$('volume-slider').addEventListener('pointerup',()=>{interacting=false;send('setInteractive',{enabled:Boolean(widget.querySelector('dialog[open]'))});});
$('volume-slider').addEventListener('blur',()=>{interacting=false;});
$('mute').addEventListener('click',()=>{if(sample)send('setAudioMute',{muted:!sample.muted});});
window.chrome?.webview?.addEventListener('message',({data})=>{if(data.type==='audioMetrics'){sample=data.sample;render();}if(data.type==='audioError'){errorUntil=Date.now()+3000;errorMessage=data.message;render();}});
document.addEventListener('visibilitychange',subscribe);
window.addEventListener('pagehide',()=>{clearTimeout(timer);send('subscribeAudio',{enabled:false,meter:false});},{once:true});
subscribe();render();
