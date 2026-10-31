import { describe, it, expect } from 'vitest';
import { classifyWordsPerSecond, computeRatio, deriveRunTimeoutMs, isTooLong } from '../../src/transcript/limits.js';

describe('deriveRunTimeoutMs', () => {
  it('max(minimo, maxDuration x factor/100)', () => {
    // 3600 s x 150 % = 5400 s > 600 s
    expect(deriveRunTimeoutMs({ minTimeoutSeconds: 600, maxDurationSeconds: 3600, timeoutFactorPct: 150 })).toBe(5_400_000);
    // 300 s x 100 % = 300 s < 600 s => minimo
    expect(deriveRunTimeoutMs({ minTimeoutSeconds: 600, maxDurationSeconds: 300, timeoutFactorPct: 100 })).toBe(600_000);
  });

  it('sin maxDuration o sin factor => el minimo', () => {
    expect(deriveRunTimeoutMs({ minTimeoutSeconds: 600 })).toBe(600_000);
    expect(deriveRunTimeoutMs({ minTimeoutSeconds: 600, maxDurationSeconds: 7200 })).toBe(600_000);
    expect(deriveRunTimeoutMs({ minTimeoutSeconds: 600, timeoutFactorPct: 200 })).toBe(600_000);
  });
});

describe('isTooLong', () => {
  it('duracion > max + tolerancia', () => {
    expect(isTooLong(3721, 3600, 120)).toBe(true);
    expect(isTooLong(3720, 3600, 120)).toBe(false);
  });

  it('sin limite o sin duracion conocida => false', () => {
    expect(isTooLong(99_999, undefined, 120)).toBe(false);
    expect(isTooLong(null, 3600, 120)).toBe(false);
  });
});

describe('computeRatio', () => {
  it('redondea a 3 decimales; denominador 0 => null', () => {
    expect(computeRatio(1, 3)).toBe(0.333);
    expect(computeRatio(5, 0)).toBeNull();
  });
});

describe('classifyWordsPerSecond', () => {
  const range = { min: 0.3, max: 5, minDurationSeconds: 30 };

  it('bajo, alto o dentro de rango', () => {
    expect(classifyWordsPerSecond(0.1, 60, range)).toBe('low');
    expect(classifyWordsPerSecond(7, 60, range)).toBe('high');
    expect(classifyWordsPerSecond(2.2, 60, range)).toBeNull();
  });

  it('clips cortos o sin dato no se evaluan', () => {
    expect(classifyWordsPerSecond(0, 10, range)).toBeNull();
    expect(classifyWordsPerSecond(null, 60, range)).toBeNull();
  });
});
