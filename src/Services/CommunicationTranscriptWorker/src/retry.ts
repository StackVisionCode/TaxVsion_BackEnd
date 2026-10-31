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
export interface RetryPolicy {
  readonly maxAttempts: number;
  readonly backoffMs: readonly number[];
}

export interface RetryOptions extends RetryPolicy {
  readonly isRetriable: (err: unknown) => boolean;
  readonly onRetry: (attempt: number, err: unknown, delayMs: number) => void;
  /**
   * Politica alternativa por tipo de error (ej. File.NotAvailable con espera
   * larga). Cada politica lleva su propio contador de intentos.
   */
  readonly policyFor?: (err: unknown) => RetryPolicy | undefined;
}

export async function withRetry<T>(fn: () => Promise<T>, opts: RetryOptions): Promise<T> {
  const attemptsByPolicy = new Map<RetryPolicy, number>();
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await fn();
    } catch (err) {
      if (!opts.isRetriable(err)) throw err;
      const policy = opts.policyFor?.(err) ?? opts;
      const policyAttempt = (attemptsByPolicy.get(policy) ?? 0) + 1;
      attemptsByPolicy.set(policy, policyAttempt);
      if (policyAttempt >= policy.maxAttempts) throw err;
      const delayMs = policy.backoffMs[policyAttempt - 1] ?? policy.backoffMs[policy.backoffMs.length - 1] ?? 0;
      opts.onRetry(attempt, err, delayMs);
      await sleep(delayMs);
    }
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
