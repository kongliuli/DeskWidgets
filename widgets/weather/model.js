export function validLocation(value) {
  return value && typeof value.name === 'string' && value.name.length > 0 && Number.isFinite(value.latitude) && Math.abs(value.latitude) <= 90 && Number.isFinite(value.longitude) && Math.abs(value.longitude) <= 180;
}
export function condition(code) {
  if (code === 0) return { label: '晴朗', scene: 'clear' };
  if ([1, 2, 3].includes(code)) return { label: ['晴间多云', '局部多云', '阴天'][code - 1], scene: 'clouds' };
  if ([45, 48].includes(code)) return { label: '雾', scene: 'clouds' };
  if ([51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 80, 81, 82].includes(code)) return { label: '降雨', scene: 'rain' };
  if ([71, 73, 75, 77, 85, 86].includes(code)) return { label: '降雪', scene: 'snow' };
  if ([95, 96, 99].includes(code)) return { label: '雷雨', scene: 'rain' };
  return { label: '天气状况未知', scene: 'clouds' };
}
export function validateWeather(data) {
  const current = data?.current;
  if (!current || typeof current.time !== 'string' || !['temperature_2m', 'apparent_temperature', 'relative_humidity_2m', 'wind_speed_10m', 'weather_code', 'is_day'].every(key => Number.isFinite(current[key]))) throw new Error('天气服务返回了不完整的数据');
  return data;
}
