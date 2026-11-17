import {mountHost,setupSettings,send,readPreference,savePreference} from './runtime.js';
export {send,readPreference,savePreference};
export const $=id=>document.getElementById(id),widget=document.querySelector('.widget');
mountHost(widget);setupSettings(widget,{background:{type:'none'}});
export function editor(markup,save){const dialog=document.createElement('dialog');dialog.className='editor';dialog.innerHTML=`<form>${markup}<p class="status" role="alert"></p><div class="row"><button type="button" data-cancel>取消</button><button>保存</button></div></form>`;widget.append(dialog);dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();dialog.onclose=()=>send('setInteractive',{enabled:false});dialog.querySelector('form').onsubmit=e=>{e.preventDefault();try{save(new FormData(e.target));dialog.close();}catch(error){dialog.querySelector('.status').textContent=error.message;}};return ()=>{send('setInteractive',{enabled:true});dialog.showModal();setTimeout(()=>dialog.querySelector('input,textarea')?.focus(),100);};}
export function persist(key,value){if(!savePreference(key,value))throw Error('保存失败，请检查本地存储空间');}

widget.dataset.ready='true';
