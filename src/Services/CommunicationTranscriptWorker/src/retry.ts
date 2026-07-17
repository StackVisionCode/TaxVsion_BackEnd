/**
 * Fase Transcript 4 — retry con backoff, usado SOLO por los 2 stages con
 * fallas transientes reales (download HTTP 5xx, upload MinIO por red/5xx).
 * ffmpeg/whisper deliberadamente no lo usan (ver pipeline.ts): sus fallas son
 * deterministicas — reintentar un exit code de ffmpeg no cambia el resultado,
 * solo quema CPU. La DLQ sigue siendo la superficie de retry para todo lo
 * demas; esto NO reemplaza eso, solo evita gastar un nack+redelivery completo
 * (que en RabbitMQ no tiene backoff propio) en errores que suelen resolverse
 * solos en unos segundos (503 de un load balancer, blip de red a MinIO).
 */
export interface RetryOptions {
  readonly maxAttempts: number;
  readonly backoffMs: readonly number[];
  readonly isRetriable: (err: unknown) => boolean;
  readonly onRetry: (attempt: number, err: unknown, delayMs: number) => void;
}

export async function withRetry<T>(fn: () => Promise<T>, opts: RetryOptions): Promise<T> {
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await fn();
    } catch (err) {
      if (attempt >= opts.maxAttempts || !opts.isRetriable(err)) {
        throw err;
      }
      const delayMs = opts.backoffMs[attempt - 1] ?? opts.backoffMs[opts.backoffMs.length - 1] ?? 0;
      opts.onRetry(attempt, err, delayMs);
      await sleep(delayMs);
    }
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
