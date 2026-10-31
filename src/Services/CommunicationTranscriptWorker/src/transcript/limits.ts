/**
 * Limites puros (INV-12 H-1) y señales de calidad (INV-14 §13.7).
 */

/** timeout por corrida = max(minimo, maxDuration × factor/100); sin ambos datos, el minimo. */
export function deriveRunTimeoutMs(input: {
  minTimeoutSeconds: number;
  maxDurationSeconds?: number | undefined;
  timeoutFactorPct?: number | undefined;
}): number {
  const { minTimeoutSeconds, maxDurationSeconds, timeoutFactorPct } = input;
  const derived =
    maxDurationSeconds !== undefined && timeoutFactorPct !== undefined && maxDurationSeconds > 0 && timeoutFactorPct > 0
      ? (maxDurationSeconds * timeoutFactorPct) / 100
      : 0;
  return Math.round(Math.max(minTimeoutSeconds, derived) * 1000);
}

/** Sin maxDurationSeconds en el evento no hay limite que aplicar. */
export function isTooLong(
  durationSeconds: number | null,
  maxDurationSeconds: number | undefined,
  toleranceSeconds: number,
): boolean {
  if (durationSeconds === null || maxDurationSeconds === undefined || maxDurationSeconds <= 0) return false;
  return durationSeconds > maxDurationSeconds + toleranceSeconds;
}

export function round3(value: number): number {
  return Math.round(value * 1000) / 1000;
}

export function computeRatio(numerator: number, denominator: number): number | null {
  if (!Number.isFinite(numerator) || !Number.isFinite(denominator) || denominator <= 0) return null;
  return round3(numerator / denominator);
}

export type WpsAnomaly = 'low' | 'high' | null;

/**
 * Palabras/segundo fuera de rango = grabacion vacia (bajo) o alucinacion
 * repetitiva (alto). Clips mas cortos que `minDurationSeconds` no se evaluan.
 */
export function classifyWordsPerSecond(
  wordsPerSecond: number | null,
  durationSeconds: number,
  range: { min: number; max: number; minDurationSeconds: number },
): WpsAnomaly {
  if (wordsPerSecond === null || durationSeconds < range.minDurationSeconds) return null;
  if (wordsPerSecond < range.min) return 'low';
  if (wordsPerSecond > range.max) return 'high';
  return null;
}
