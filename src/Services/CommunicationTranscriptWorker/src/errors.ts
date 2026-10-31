/**
 * Errores tipados del pipeline. Viven fuera de media/ y whisper/ porque los
 * tests mockean esos modulos enteros y `instanceof` contra un mock no existe.
 */
export class NoAudioStreamError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'NoAudioStreamError';
  }
}

/** whisper-cli o ffmpeg superaron el timeout derivado de la duracion maxima. */
export class ProcessTimeoutError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ProcessTimeoutError';
  }
}

/** Duracion real por encima de maxDurationSeconds + tolerancia (INV-12 H-1). */
export class TooLongError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'TooLongError';
  }
}

/** execFile mata el hijo al vencer `timeout`: `killed` true y `signal` seteado. */
export function isExecTimeout(err: unknown): boolean {
  const e = err as { killed?: unknown; signal?: unknown } | null;
  return e?.killed === true && typeof e.signal === 'string';
}
