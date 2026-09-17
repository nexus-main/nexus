import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { fitLegendName, formatLegendValue } from './legend-text.ts';

describe('legend text', () => {
  it('keeps full names when they fit and shortens the middle when they do not', () => {
    const measure = (text: string) => text.length;
    assert.equal(fitLegendName('Temperature', 11, measure), 'Temperature');
    assert.equal(fitLegendName('Temperature', 8, measure), 'Tem...re');
    assert.equal(fitLegendName('Temperature', 3, measure), '...');
    assert.equal(fitLegendName('Temperature', 2, measure), '');
    assert.equal(fitLegendName('Temperature', 0, measure), '');
  });

  it('fits variable-width glyphs while retaining both ends', () => {
    const measure = (text: string) => Array.from(text).reduce((width, char) => width + (char === 'W' ? 3 : 1), 0);
    const name = 'WWWWW.sensor.iiiii';
    const shortened = fitLegendName(name, 15, measure);
    assert.ok(measure(shortened) <= 15);
    assert.match(shortened, /^W+\.\.\.i+$/);
    assert.equal(fitLegendName(name, 100, measure), name);
  });

  it('preserves normal decimal precision and bounds extreme values to the reserved number column', () => {
    assert.equal(formatLegendValue(23.481, 3), '23.481');
    assert.equal(formatLegendValue(-0.125, 4), '-0.1250');
    for (const value of [0, -1e-38, 1e-38, -3.4028235e38, 3.4028235e38]) {
      const text = formatLegendValue(value, 100);
      assert.ok(text.length <= 14, text);
      assert.ok(Math.abs(Number(text) - value) <= Math.abs(value) * 1e-7);
    }
  });
});
