export function layoutFor(width,height,entries,maximum='auto'){
  height=Number.isFinite(height)?Math.max(1,height):1;
  const capacity=Math.max(1,Math.floor((height+6)/50)),limit=[3,4,5].includes(Number(maximum))?Number(maximum):100;
  const count=Math.max(0,Math.min(entries.length,capacity,limit));return {count,columns:1,rows:Math.max(1,count)};
}
