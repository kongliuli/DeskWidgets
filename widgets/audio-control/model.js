export function normalizeConfig(value={}) { return {meter:value.meter!==false,background:value.background??{type:'none'}}; }
export function peakToDbfs(peak){return Number.isFinite(peak)&&peak>0?20*Math.log10(Math.min(1,peak)):null;}
export function formatDbfs(peak){if(!Number.isFinite(peak))return'-- dBFS';if(peak<=0)return'−∞ dBFS';const db=peakToDbfs(peak);return db < -80 ? '≤−80 dBFS' : `${db.toFixed(1)} dBFS`;}
