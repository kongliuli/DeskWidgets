import {$,editor,readPreference,persist} from 'https://assets.widgets.local/utility.js';
let state=readPreference('focus',{task:'专注一件事',minutes:25,remaining:1500000,end:null});
if(!state||!Number.isFinite(state.minutes)||state.minutes<1||state.minutes>240)state={task:'专注一件事',minutes:25,remaining:1500000,end:null};
function left(){return state.end?Math.max(0,state.end-Date.now()):state.remaining;}
function save(){persist('focus',state);render();}
function render(){const ms=left(),seconds=Math.ceil(ms/1000);$('remaining').textContent=`${String(Math.floor(seconds/60)).padStart(2,'0')}:${String(seconds%60).padStart(2,'0')}`;$('task').textContent=state.task;$('toggle').textContent=state.end?'暂停':'开始';if(ms===0){$('status').textContent='本轮专注已完成';if(state.end){state.end=null;state.remaining=0;persist('focus',state);}}}
$('toggle').onclick=()=>{if(state.end){state.remaining=left();state.end=null;}else{if(left()===0)state.remaining=state.minutes*60000;state.end=Date.now()+state.remaining;}save();};
$('reset').onclick=()=>{state.end=null;state.remaining=state.minutes*60000;$('status').textContent='已重置';save();};
$('edit').onclick=editor('<label>本轮待办<input name="task" maxlength="150" required></label><label>分钟<input name="minutes" type="number" min="1" max="240" value="25" required></label>',data=>{const minutes=Number(data.get('minutes'));if(!Number.isInteger(minutes)||minutes<1||minutes>240)throw Error('请输入 1–240 分钟');state={task:String(data.get('task')),minutes,remaining:minutes*60000,end:null};save();});
let timer;function visibility(){clearInterval(timer);if(!document.hidden){render();timer=setInterval(render,1000);}}document.addEventListener('visibilitychange',visibility);window.addEventListener('pagehide',()=>clearInterval(timer));visibility();
