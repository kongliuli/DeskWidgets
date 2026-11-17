import { percent, formatPercent } from './model.js';
export function drawChart(canvas, layout, metrics, sample, history) {
  const rect=canvas.getBoundingClientRect(), dpr=devicePixelRatio||1, w=rect.width,h=rect.height;
  if(w<1||h<1)return;
  if(canvas.width!==Math.round(w*dpr)||canvas.height!==Math.round(h*dpr)){canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);}
  const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,w,h);ctx.font='10px "Segoe UI",sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';
  if(layout==='rings'){
    const cell=w/Math.max(1,metrics.length),r=Math.max(8,Math.min(cell*.33,h*.37)),cy=h*.47;
    metrics.forEach((metric,index)=>{const x=cell*(index+.5),value=percent(sample?.[metric.field]);ctx.lineWidth=Math.max(4,r*.14);ctx.strokeStyle='#ffffff18';ctx.beginPath();ctx.arc(x,cy,r,0,Math.PI*2);ctx.stroke();if(value!==null){ctx.strokeStyle=metric.color;ctx.lineCap='round';ctx.beginPath();ctx.arc(x,cy,r,-Math.PI/2,-Math.PI/2+Math.PI*2*value/100);ctx.stroke();ctx.lineCap='butt';}ctx.fillStyle='#eef7ff';ctx.font=`${Math.max(10,r*.55)}px "Segoe UI",sans-serif`;ctx.fillText(formatPercent(value),x,cy);ctx.font='10px "Segoe UI",sans-serif';ctx.fillStyle=metric.color;ctx.fillText(metric.label,x,Math.min(h-6,cy+r+14));});
  }else if(layout==='radar'){
    const x=w/2,y=h*.53,r=Math.max(8,Math.min(w*.3,h*.35));
    const point=(index,scale)=>[x+Math.cos(-Math.PI/2+index*Math.PI*2/3)*r*scale,y+Math.sin(-Math.PI/2+index*Math.PI*2/3)*r*scale];
    ctx.strokeStyle='#ffffff23';ctx.lineWidth=1;for(let ring=1;ring<=4;ring++){ctx.beginPath();metrics.forEach((metric,i)=>{const p=point(i,ring/4);i?ctx.lineTo(...p):ctx.moveTo(...p);});ctx.closePath();ctx.stroke();}
    metrics.forEach((metric,i)=>{const p=point(i,1);ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(...p);ctx.stroke();const label=point(i,1.25);ctx.fillStyle=metric.color;ctx.fillText(metric.label,label[0],Math.max(6,Math.min(h-6,label[1])));});
    if(metrics.every(metric=>percent(sample?.[metric.field])!==null)){ctx.beginPath();metrics.forEach((metric,i)=>{const p=point(i,sample[metric.field]/100);i?ctx.lineTo(...p):ctx.moveTo(...p);});ctx.closePath();ctx.fillStyle='#7dd3fc25';ctx.strokeStyle='#a6dfff';ctx.fill();ctx.stroke();}else{ctx.fillStyle='#d6e7f4';ctx.fillText('等待有效样本',x,y);}
  }else if(layout==='lines'){
    const left=24,right=w-4,top=7,bottom=h-16, end=sample?.timestamp??Date.now(),start=end-120000;
    ctx.font='8px "Segoe UI",sans-serif';for(const value of [0,50,100]){const y=bottom-(bottom-top)*value/100;ctx.strokeStyle='#ffffff13';ctx.beginPath();ctx.moveTo(left,y);ctx.lineTo(right,y);ctx.stroke();ctx.fillStyle='#aac0d2';ctx.textAlign='right';ctx.fillText(String(value),left-5,y);}
    for(const metric of metrics){ctx.strokeStyle=metric.color;ctx.lineWidth=1.6;ctx.beginPath();let previous=null,lastPoint=null;for(const entry of history){const value=percent(entry[metric.field]);if(entry.timestamp<start||value===null){previous=null;continue;}const x=left+(entry.timestamp-start)/120000*(right-left),y=bottom-value/100*(bottom-top);if(previous!==null&&entry.timestamp-previous<=6000)ctx.lineTo(x,y);else ctx.moveTo(x,y);previous=entry.timestamp;lastPoint=[x,y];}ctx.stroke();if(lastPoint){ctx.fillStyle=metric.color;ctx.beginPath();ctx.arc(...lastPoint,2,0,Math.PI*2);ctx.fill();}}
    ctx.fillStyle='#aac0d2';ctx.textAlign='left';ctx.fillText('−2 分钟',left,h-4);ctx.textAlign='right';ctx.fillText('现在',right,h-4);
  }
}
