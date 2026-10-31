/** Reintento con backoff solo para fallas transitorias de red/MinIO/Rabbit (misma política que el worker). */
export interface RetryOptions {
  readonly maxAttempts: number;
  readonly backoffMs: readonly number[];
  readonly isRetriable: (err: unknown) => boolean;
  readonly onRetry?: (attempt: number, err: unknown, delayMs: number) => void;
  readonly sleep?: (ms: number) => Promise<void>;
}

export async function withRetry<T>(fn: () => Promise<T>, opts: RetryOptions): Promise<T> {
  const sleep = opts.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await fn();
    } catch (err) {
      if (attempt >= opts.maxAttempts || !opts.isRetriable(err)) throw err;
      const delayMs = opts.backoffMs[attempt - 1] ?? opts.backoffMs[opts.backoffMs.length - 1] ?? 0;
      opts.onRetry?.(attempt, err, delayMs);
      await sleep(delayMs);
    }
  }
}

// Mismos códigos que el transcript worker: red de Node o 5xx/backpressure S3. Credenciales o política
// mal configuradas no están: un reintento no las arregla.
export const RETRIABLE_CODES = new Set([
  'ECONNRESET',
  'ETIMEDOUT',
  'ECONNREFUSED',
  'EPIPE',
  'EAI_AGAIN',
  'InternalError',
  'SlowDown',
  'RequestTimeout',
  'ServiceUnavailable',
]);

export function isTransientError(err: unknown): boolean {
  const code = (err as { code?: unknown } | null)?.code;
  return typeof code === 'string' && RETRIABLE_CODES.has(code);
}
