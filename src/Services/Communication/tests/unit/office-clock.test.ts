import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  civilDaysBetween,
  dayBounds,
  localDate,
  offsetMinutes,
  wallToUtc,
  yearMonth,
} from '../../src/application/services/office-clock.js';

// Mismos vectores que las pruebas .NET (deploy/tests/Shared/temporal-vectors.json).
const here = dirname(fileURLToPath(import.meta.url));
const vectors = JSON.parse(
  readFileSync(resolve(here, '../../../../../deploy/tests/Shared/temporal-vectors.json'), 'utf8'),
) as {
  utcToLocal: { zone: string; utc: string; civilDate: string; offset: string }[];
  wallToUtc: { zone: string; local: string; kind: string; utc?: string; onExpandUtc?: string }[];
  civilDayBounds: { zone: string; date: string; startUtc: string; endUtc: string }[];
};

function offsetText(minutes: number): string {
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  return `${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}

describe('officeClock', () => {
  it.each(vectors.utcToLocal)('fecha civil y offset de $utc en $zone', v => {
    expect(localDate(new Date(v.utc), v.zone)).toBe(v.civilDate);
    expect(offsetText(offsetMinutes(new Date(v.utc), v.zone))).toBe(v.offset);
  });

  it.each(vectors.civilDayBounds)('límites del día $date en $zone', v => {
    const bounds = dayBounds(v.date, v.zone);
    expect(bounds.startUtc.toISOString()).toBe(new Date(v.startUtc).toISOString());
    expect(bounds.endUtc.toISOString()).toBe(new Date(v.endUtc).toISOString());
  });

  it.each(vectors.wallToUtc)('hora de pared $local ($kind) en $zone al expandir', v => {
    const expected = v.utc ?? v.onExpandUtc!;
    expect(wallToUtc(v.local.slice(0, 10), v.local.slice(11), v.zone, 'shiftForward')?.toISOString()).toBe(new Date(expected).toISOString());
  });

  it('rechaza una hora inexistente al crear y acepta la ambigua tomando la primera', () => {
    expect(wallToUtc('2026-03-08', '02:30', 'America/New_York', 'reject')).toBeNull();
    expect(wallToUtc('2026-11-01', '01:30', 'America/New_York', 'reject')?.toISOString()).toBe('2026-11-01T05:30:00.000Z');
  });

  it('cuenta días de calendario en la zona, no bloques de 24 h', () => {
    const evening = new Date('2026-11-13T01:30:00Z');
    const morning = new Date('2026-11-13T13:00:00Z');
    expect(civilDaysBetween(evening, morning, 'America/New_York')).toBe(1);
    expect(civilDaysBetween(evening, morning, 'Etc/UTC')).toBe(0);
  });

  it('el mes de un instante es el de la zona', () => {
    // 03:00 UTC del 1-dic sigue siendo noviembre en Nueva York.
    expect(yearMonth(new Date('2026-12-01T03:00:00Z'), 'America/New_York')).toBe('2026-11');
    expect(yearMonth(new Date('2026-12-01T03:00:00Z'), 'Etc/UTC')).toBe('2026-12');
  });
});
