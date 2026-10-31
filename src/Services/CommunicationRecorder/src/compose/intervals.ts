/** Intervalo semiabierto [start, end) en ms de la ventana de grabación. */
export interface Interval {
  readonly start: number;
  readonly end: number;
}

export function normalize(list: readonly Interval[], mergeGap = 0): Interval[] {
  const sorted = list.filter((i) => i.end > i.start).sort((a, b) => a.start - b.start || a.end - b.end);
  const out: Interval[] = [];
  for (const cur of sorted) {
    const last = out[out.length - 1];
    if (last && cur.start <= last.end + mergeGap) {
      out[out.length - 1] = { start: last.start, end: Math.max(last.end, cur.end) };
    } else {
      out.push({ start: cur.start, end: cur.end });
    }
  }
  return out;
}

export function union(...lists: (readonly Interval[])[]): Interval[] {
  return normalize(lists.flat());
}

export function intersect(a: readonly Interval[], b: readonly Interval[]): Interval[] {
  const x = normalize(a);
  const y = normalize(b);
  const out: Interval[] = [];
  let i = 0;
  let j = 0;
  while (i < x.length && j < y.length) {
    const p = x[i] as Interval;
    const q = y[j] as Interval;
    const start = Math.max(p.start, q.start);
    const end = Math.min(p.end, q.end);
    if (end > start) out.push({ start, end });
    if (p.end < q.end) i += 1;
    else j += 1;
  }
  return out;
}

export function complement(list: readonly Interval[], start: number, end: number): Interval[] {
  const out: Interval[] = [];
  let cursor = start;
  for (const i of normalize(list)) {
    if (i.end <= start || i.start >= end) continue;
    if (i.start > cursor) out.push({ start: cursor, end: Math.min(i.start, end) });
    cursor = Math.max(cursor, i.end);
  }
  if (cursor < end) out.push({ start: cursor, end });
  return out;
}

export function subtract(a: readonly Interval[], b: readonly Interval[], start: number, end: number): Interval[] {
  return intersect(a, complement(b, start, end));
}

export function clip(list: readonly Interval[], start: number, end: number): Interval[] {
  return intersect(list, [{ start, end }]);
}

export function contains(list: readonly Interval[], t: number): boolean {
  return list.some((i) => t >= i.start && t < i.end);
}

export interface Toggle {
  readonly at: number;
  readonly value: boolean;
}

/**
 * Convierte cambios de estado ordenados en intervalos donde el estado es `true`, recortados a
 * [0, windowEnd). Los cambios anteriores a 0 fijan el estado inicial (filas previas al inicio).
 */
export function intervalsFromToggles(toggles: readonly Toggle[], initial: boolean, windowEnd: number): Interval[] {
  const out: Interval[] = [];
  let state = initial;
  let since = Number.NEGATIVE_INFINITY;
  for (const t of toggles) {
    if (t.value === state) continue;
    if (state) out.push({ start: since, end: t.at });
    state = t.value;
    since = t.at;
  }
  if (state) out.push({ start: since, end: Number.POSITIVE_INFINITY });
  return clip(out, 0, windowEnd);
}
