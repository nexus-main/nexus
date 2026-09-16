export function fitLegendName(name: string, width: number, measure: (text: string) => number): string {
  if (width <= 0) return '';
  if (measure(name) <= width) return name;
  if (measure('...') > width) return '';
  const characters = Array.from(name);
  let low = 0;
  let high = characters.length - 1;
  let result = '...';
  while (low <= high) {
    const count = Math.floor((low + high) / 2);
    const start = Math.ceil(count / 2);
    const end = Math.floor(count / 2);
    const candidate = characters.slice(0, start).join('') + '...' + (end ? characters.slice(-end).join('') : '');
    if (measure(candidate) <= width) {
      result = candidate;
      low = count + 1;
    } else high = count - 1;
  }
  return result;
}

export function formatLegendValue(value: number, digits: number): string {
  const fixed = value.toFixed(digits);
  return fixed.length <= 14 ? fixed : value.toExponential(7);
}
