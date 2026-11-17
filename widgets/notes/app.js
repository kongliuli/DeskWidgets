import {$,widget,send,readPreference,savePreference} from 'https://assets.widgets.local/utility.js';
$('note').value=String(readPreference('note',''));let timer;
function save(){clearTimeout(timer);$('status').textContent=savePreference('note',$('note').value)?'已保存 · 本实例纯文本':'保存失败，请先复制备份内容';}
$('note').oninput=()=>{clearTimeout(timer);timer=setTimeout(save,400);};$('note').onfocus=()=>send('setInteractive',{enabled:true});$('note').onblur=()=>{save();send('setInteractive',{enabled:false});};window.addEventListener('pagehide',save);
window.chrome?.webview?.addEventListener('message',({data})=>{if(data.type==='hostState')$('note').readOnly=data.locked;});

$('note').addEventListener('pointerdown',()=>{if(!widget.classList.contains('locked'))send('setInteractive',{enabled:true});});

let records=[],dragPointer=null,dragElement=null;
function status(message){$('status').textContent=message;$('history-status').textContent=message;}
function finishDrag(cancel){if(dragPointer===null)return;const pointer=dragPointer,element=dragElement;dragPointer=null;dragElement=null;send(cancel?'cancelNoteDrag':'endNoteDrag');if(element?.hasPointerCapture?.(pointer))element.releasePointerCapture(pointer);}
function renderHistory(){
  $('history-count').textContent=records.length;$('history-list').replaceChildren();
  if(!records.length){const p=document.createElement('p');p.textContent='还没有历史便签。先写下内容，再点“保存到历史”。';$('history-list').append(p);}
  for(const record of records){
    const entry=document.createElement('section');entry.className='history-entry';
    const date=document.createElement('div');date.className='history-date';date.textContent=new Date(record.createdAt).toLocaleString('zh-CN')+(record.pinned?' · 已在桌面':'');
    const preview=document.createElement('p');preview.className='history-preview';preview.textContent=record.preview;
    const actions=document.createElement('div');actions.className='row functional-controls';
    const read=document.createElement('button');read.textContent='查看全文';read.onclick=()=>send('readNoteHistory',{id:record.id});
    const pin=document.createElement('button');pin.textContent=record.pinned?'关闭桌面便签':'拖到桌面';pin.className='drag-note';
    if(record.pinned)pin.onclick=()=>send('closeDesktopNote',{id:record.id});
    else {pin.onpointerdown=e=>{if(e.button!==0||widget.classList.contains('locked'))return;e.preventDefault();dragPointer=e.pointerId;dragElement=pin;try{pin.setPointerCapture(e.pointerId);}catch{}send('startNoteDrag',{id:record.id});};pin.onpointerup=()=>finishDrag(false);pin.onpointercancel=()=>finishDrag(true);pin.onlostpointercapture=()=>finishDrag(true);pin.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();send('startNoteDrag',{id:record.id});send('endNoteDrag');}};}
    const remove=document.createElement('button');remove.textContent='删除历史';remove.disabled=record.pinned;remove.onclick=()=>send('deleteNoteHistory',{id:record.id});
    actions.append(read,pin,remove);entry.append(date,preview,actions);$('history-list').append(entry);
  }
}
$('save-history').onclick=()=>{save();if(!$('note').value.trim()){status('先写一点内容再保存到历史');return;}send('saveNoteHistory',{text:$('note').value});};
$('open-history').onclick=()=>{send('requestNotes');send('setInteractive',{enabled:true});$('history-text').hidden=true;$('history').showModal();};
$('close-history').onclick=()=>$('history').close();$('history').addEventListener('close',()=>{finishDrag(true);send('setInteractive',{enabled:false});});
document.addEventListener('keydown',e=>{if(e.key==='Escape')finishDrag(true);});window.addEventListener('pagehide',()=>finishDrag(true));
window.chrome?.webview?.addEventListener('message',({data})=>{if(data.type==='notesState'){records=data.notes;renderHistory();}if(data.type==='notesError')status(data.message);if(data.type==='noteHistorySaved')status('已保存到历史便签');if(data.type==='noteContent'){$('history-text').value=data.text;$('history-text').hidden=false;}});
send('requestNotes');
