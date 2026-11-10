import { describe, expect, it } from 'vitest';
import { parseMeetingRange } from '../../src/api/http/schemas/meeting-range.js';
import { scheduledBetweenWhere } from '../../src/infrastructure/persistence/meeting-range-where.js';

// B2 (Calendar): el listado de reuniones acepta un rango para pintar el calendario.
describe('parseMeetingRange', () => {
  it('sin from ni to no filtra', () => {
    expect(parseMeetingRange(undefined, undefined)).toEqual({ ok: true, range: undefined });
  });

  it('acepta un rango valido', () => {
    const result = parseMeetingRange('2026-11-01T00:00:00Z', '2026-12-13T00:00:00Z');
    expect(result).toEqual({
      ok: true,
      range: { fromUtc: new Date('2026-11-01T00:00:00Z'), toUtc: new Date('2026-12-13T00:00:00Z') },
    });
  });

  it('rechaza un solo extremo, un rango invertido o mayor a 62 dias', () => {
    expect(parseMeetingRange('2026-11-01T00:00:00Z', undefined).ok).toBe(false);
    expect(parseMeetingRange('2026-11-10T00:00:00Z', '2026-11-01T00:00:00Z').ok).toBe(false);
    expect(parseMeetingRange('2026-11-01T00:00:00Z', '2027-01-03T00:00:00Z').ok).toBe(false);
  });
});

describe('scheduledBetweenWhere', () => {
  it('usa la hora programada y, si no hay, la de inicio', () => {
    const range = { fromUtc: new Date('2026-11-01T00:00:00Z'), toUtc: new Date('2026-11-08T00:00:00Z') };
    expect(scheduledBetweenWhere(range)).toEqual({
      OR: [
        { ScheduledForUtc: { gte: range.fromUtc, lt: range.toUtc } },
        { ScheduledForUtc: null, StartedAtUtc: { gte: range.fromUtc, lt: range.toUtc } },
      ],
    });
  });
});
