import {layoutFor} from './layout.js';
import {mountHost,setupSettings,loadManifest,send} from 'https://assets.widgets.local/runtime.js';
const widget=document.querySelector('.widget'),$=id=>document.getElementById(id);mountHost(widget);
let entries=[],config,ready=false;
const manifest=await loadManifest();
function subscribe(){send('subscribeClipboard',{enabled:!config.paused});}
function configure(value){config={...value,count:[3,4,5].includes(Number(value.count))?Number(value.count):'auto',paused:value.paused===true};if(ready){subscribe();render();}}
configure(setupSettings(widget,manifest.settings,'<label>常驻显示<select name="count" data-setting><option value="auto">随卡片大小自动</option><option value="3">最多 3 条</option><option value="4">最多 4 条</option><option value="5">最多 5 条</option></select></label><label><input type="checkbox" name="paused" data-setting>暂停缓存</label>',configure,data=>({count:data.get('count')==='auto'?'auto':Number(data.get('count')),paused:data.has('paused')})));ready=true;
function render(){
  $('count').textContent=String(entries.length);$('empty').hidden=entries.length>0;$('recent').hidden=!entries.length;$('recent').style.setProperty('--viewport-limit',config.count==='auto'?'none':`${config.count*50-6}px`);const bounds=$('recent').getBoundingClientRect(),layout=layoutFor(bounds.width,bounds.height,entries,config.count);widget.dataset.visibleCount=String(layout.count);$('visible-count').textContent=`条 · 可见 ${layout.count} 条`;
  function fill(element,items){const existing=new Map([...element.children].map(button=>[button.dataset.id,button]));const nodes=[];for(const item of items){let button=existing.get(item.id);if(!button){button=document.createElement('button');button.dataset.id=item.id;
    if(item.kind==='image'){button.className='image-entry';const image=document.createElement('img');image.src=item.thumbnail;image.alt=`图片 ${item.width} × ${item.height}`;image.loading='lazy';const caption=document.createElement('span');caption.textContent=`${item.width} × ${item.height} · 点击复制图片`;button.append(image,caption);}
    else button.textContent=item.text;
    button.addEventListener('click',()=>send('copyClipboardEntry',{id:item.id}));}
    button.disabled=widget.classList.contains('locked');nodes.push(button);}element.replaceChildren(...nodes);}
  fill($('recent'),entries);if($('history').open){const query=$('search').value.toLocaleLowerCase();fill($('results'),entries.filter(item=>item.text.toLocaleLowerCase().includes(query)));}else $('results').replaceChildren();
  $('pause').textContent=config.paused?'恢复缓存':'暂停缓存';
}
$('pause').onclick=()=>{config.paused=!config.paused;try{localStorage.setItem('settings',JSON.stringify(config));}catch{$('status').textContent='暂停状态未能保存';}subscribe();render();$('status').textContent=config.paused?'缓存已暂停，已有内容仍可点选复制':'缓存已恢复';};
$('clear').onclick=()=>send('clearClipboardCache');$('history-open').onclick=()=>{send('requestClipboard');send('setInteractive',{enabled:true});$('history').showModal();render();setTimeout(()=>$('search').focus(),80);};$('history-close').onclick=()=>$('history').close();$('history').addEventListener('close',()=>send('setInteractive',{enabled:false}));let searchTimer;$('search').oninput=()=>{clearTimeout(searchTimer);searchTimer=setTimeout(render,120);};
widget.querySelector('[data-action=settings]').addEventListener('click',()=>{const form=widget.querySelector('.widget-settings form');form.elements.paused.checked=config.paused;form.elements.count.value=config.count;});
window.chrome?.webview?.addEventListener('message',({data})=>{if(data.type==='clipboardState'){entries=data.entries;if(!document.hidden)render();$('status').textContent=config.paused?'缓存已暂停':data.status;}if(data.type==='clipboardResult'||data.type==='clipboardError')$('status').textContent=data.message;});
const observer=new ResizeObserver(()=>render());observer.observe($('recent'));
document.addEventListener('visibilitychange',()=>{if(!document.hidden){subscribe();send('requestClipboard');render();}});window.addEventListener('pagehide',()=>{observer.disconnect();send('subscribeClipboard',{enabled:false});});subscribe();send('requestClipboard');render();widget.dataset.ready='true';
