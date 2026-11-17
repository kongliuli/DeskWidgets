import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=async path=>import(`data:text/javascript;base64,${Buffer.from(readFileSync(new URL(path,import.meta.url),'utf8')).toString('base64')}`);
const monitor=await load('../widgets/system-monitor/model.js');
assert.equal(monitor.effectiveLayout(monitor.normalizeConfig({layout:'radar',gpu:false})),'bars');
assert.equal(monitor.normalizeConfig({layout:'unknown'}).layout,'bars');assert.equal(monitor.percent(null),null);assert.equal(monitor.percent(0),0);assert.equal(monitor.percent(101),null);
const history=[];for(let i=0;i<100;i++)monitor.appendHistory(history,{timestamp:i,cpuPercent:i});assert.equal(history.length,60);assert.equal(history[0].timestamp,40);monitor.appendHistory(history,{timestamp:50});assert.equal(history.length,60);
const audio=await load('../widgets/audio-control/model.js');assert.equal(audio.formatDbfs(0),'−∞ dBFS');assert.equal(audio.formatDbfs(null),'-- dBFS');assert.ok(Math.abs(audio.peakToDbfs(.5)+6.0205999)<.001);assert.equal(audio.normalizeConfig({meter:false}).meter,false);
console.log('PASS: system layouts, unavailable vs zero, bounded timestamp history, dBFS/silence and meter switch');
