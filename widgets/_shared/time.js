const formatters = new Map();
export function clockParts(date, timeZone) {
  if (!formatters.has(timeZone)) formatters.set(timeZone, new Intl.DateTimeFormat('en-CA', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23', timeZoneName: 'short'
  }));
  const parts = Object.fromEntries(formatters.get(timeZone).formatToParts(date).map(p => [p.type, p.value]));
  return { time: `${parts.hour}:${parts.minute}:${parts.second}`, date: `${parts.year}-${parts.month}-${parts.day}`, zone: parts.timeZoneName };
}
export function dayDifference(localDate, utcDate) {
  const days = Math.round((Date.parse(`${localDate}T00:00:00Z`) - Date.parse(`${utcDate}T00:00:00Z`)) / 86400000);
  return days === 0 ? '同日' : `${days > 0 ? '+' : '−'}${Math.abs(days)}天`;
}
