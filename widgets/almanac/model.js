export function almanacFor(date, Solar) {
  const p = Object.fromEntries(new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date).map(part => [part.type, part.value]));
  const solar = Solar.fromYmd(Number(p.year), Number(p.month), Number(p.day));
  const lunar = solar.getLunar();
  const next = lunar.getNextJieQi(true);
  return {
    key: `${p.year}-${p.month}-${p.day}`, day: p.day, month: `${p.year} 年 ${Number(p.month)} 月`,
    weekday: new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', weekday: 'long' }).format(date),
    lunarDate: `${lunar.getMonthInChinese()}月${lunar.getDayInChinese()}`,
    year: `${lunar.getYearInGanZhi()}年 · ${lunar.getYearShengXiao()}年`,
    ganzhi: `${lunar.getYearInGanZhi()}年 ${lunar.getMonthInGanZhi()}月 ${lunar.getDayInGanZhi()}日`,
    festival: [...lunar.getFestivals(), ...solar.getFestivals(), lunar.getJieQi()].filter(Boolean).join(' · ') || '岁月有序，日有所期',
    yi: lunar.getDayYi(), ji: lunar.getDayJi(),
    term: `${next.getName()} · ${next.getSolar().toYmd()}`,
    chong: `冲${lunar.getDayChongDesc()} · 煞${lunar.getDaySha()}`,
    god: `${lunar.getDayTianShen()} · ${lunar.getDayTianShenType()}`,
    wealth: lunar.getDayPositionCaiDesc(), pengzu: `${lunar.getPengZuGan()}；${lunar.getPengZuZhi()}`
  };
}
