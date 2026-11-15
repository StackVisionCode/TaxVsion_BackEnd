/**
 * Cálculo civil puro con zonas IANA (Intl). Equivale a BuildingBlocks.TimeZones de .NET y pasa los mismos
 * vectores (deploy/tests/Shared/temporal-vectors.json). Nunca usa la zona del proceso: la zona va explícita.
 * Política de horas (Calendar C-T9): la inexistente se rechaza o corre hacia adelante; la ambigua toma la primera.
 */

export type NonexistentTimePolicy = 'reject' | 'shiftForward';

const HOUR_MS = 3_600_000;

/** Offset de la zona en ese instante, en minutos (respeta el horario de verano). */
export function offsetMinutes(instant: Date, timeZone: string): number {
  const name = new Intl.DateTimeFormat('en-US', { timeZone, timeZoneName: 'longOffset' })
    .formatToParts(instant)
    .find(part => part.type === 'timeZoneName')?.value ?? 'GMT';
  const match = /GMT([+-])(\d{1,2})(?::(\d{2}))?/.exec(name);
  if (!match) return 0;
  const minutes = Number(match[2]) * 60 + Number(match[3] ?? 0);
  return match[1] === '-' ? -minutes : minutes;
}

/** Fecha civil `yyyy-MM-dd` de un instante en la zona. */
export function localDate(instant: Date, timeZone: string): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(instant);
  const part = (type: string) => parts.find(p => p.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}

/** Mes civil `yyyy-MM` de un instante en la zona. */
export function yearMonth(instant: Date, timeZone: string): string {
  return localDate(instant, timeZone).slice(0, 7);
}

/** Días de calendario entre dos instantes vistos en la zona (no bloques de 24 h). */
export function civilDaysBetween(from: Date, to: Date, timeZone: string): number {
  return Math.round((Date.parse(`${localDate(to, timeZone)}T00:00:00Z`) - Date.parse(`${localDate(from, timeZone)}T00:00:00Z`)) / 86_400_000);
}

/**
 * Hora de pared (`yyyy-MM-dd`, `HH:mm`) → instante UTC. Devuelve null solo si la hora no existe y la
 * política es `reject`.
 */
export function wallToUtc(date: string, time: string, timeZone: string, policy: NonexistentTimePolicy): Date | null {
  const wallAsUtc = Date.parse(`${date}T${time}:00Z`);
  const before = offsetMinutes(new Date(wallAsUtc - 12 * HOUR_MS), timeZone);
  const after = offsetMinutes(new Date(wallAsUtc + 12 * HOUR_MS), timeZone);

  // Un candidato es válido si, en ese instante, la zona tiene justo el offset que se supuso.
  const valid = [...new Set([before, after])]
    .map(offset => wallAsUtc - offset * 60_000)
    .filter(instant => offsetMinutes(new Date(instant), timeZone) * 60_000 === wallAsUtc - instant)
    .sort((a, b) => a - b);

  const first = valid[0];
  if (first !== undefined) return new Date(first);
  // Hora inexistente: con el offset de antes del salto se cae del otro lado, corrido el largo del salto.
  return policy === 'reject' ? null : new Date(wallAsUtc - before * 60_000);
}

/** Inicio (medianoche local, o el primer instante si no existe) y fin (23:59:59 local) de un día civil. */
export function dayBounds(date: string, timeZone: string): { startUtc: Date; endUtc: Date } {
  const next = new Date(Date.parse(`${date}T00:00:00Z`) + 86_400_000).toISOString().slice(0, 10);
  const startUtc = wallToUtc(date, '00:00', timeZone, 'shiftForward')!;
  const nextStart = wallToUtc(next, '00:00', timeZone, 'shiftForward')!;
  return { startUtc, endUtc: new Date(nextStart.getTime() - 1000) };
}
