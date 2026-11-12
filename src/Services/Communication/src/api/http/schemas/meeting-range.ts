// Rango opcional del listado de reuniones (vista calendario). Tope igual al de Calendar y Tasks.
const MAX_RANGE_MS = 62 * 24 * 60 * 60 * 1000;

export interface MeetingRange {
  readonly fromUtc: Date;
  readonly toUtc: Date;
}

export type MeetingRangeResult = { ok: true; range: MeetingRange | undefined } | { ok: false };

export function parseMeetingRange(from: string | undefined, to: string | undefined): MeetingRangeResult {
  if (from === undefined && to === undefined) return { ok: true, range: undefined };
  if (from === undefined || to === undefined) return { ok: false };

  const fromUtc = new Date(from);
  const toUtc = new Date(to);
  const span = toUtc.getTime() - fromUtc.getTime();
  if (Number.isNaN(span) || span <= 0 || span > MAX_RANGE_MS) return { ok: false };
  return { ok: true, range: { fromUtc, toUtc } };
}
