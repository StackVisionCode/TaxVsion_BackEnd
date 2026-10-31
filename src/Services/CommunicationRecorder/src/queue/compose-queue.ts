/** Subconjunto de ioredis que usa la cola (permite un doble en memoria en los tests). */
export interface QueueRedis {
  lpush(key: string, ...values: string[]): Promise<number>;
  lmove(source: string, destination: string, from: 'LEFT' | 'RIGHT', to: 'LEFT' | 'RIGHT'): Promise<string | null>;
  blmove(
    source: string,
    destination: string,
    from: 'LEFT' | 'RIGHT',
    to: 'LEFT' | 'RIGHT',
    timeout: number,
  ): Promise<string | null>;
  lrem(key: string, count: number, value: string): Promise<number>;
  lrange(key: string, start: number, stop: number): Promise<string[]>;
  llen(key: string): Promise<number>;
}

export interface ComposeJob {
  readonly jobId: string;
  readonly sessionId: string;
  readonly mode: 'compose' | 'recompose';
  readonly enqueuedAtUtc: string;
}

/**
 * Cola de composición en Redis: `{prefix}:compose:queue` (pendientes) y `{prefix}:compose:processing`
 * (en curso). BLMOVE pasa el trabajo a "en curso" de forma atómica; al terminar se quita con LREM. Si
 * el proceso cae, `recover()` al arrancar devuelve lo que quedó en curso a pendientes.
 */
export class ComposeQueue {
  readonly queueKey: string;
  readonly processingKey: string;

  constructor(
    private readonly redis: QueueRedis,
    /** Conexión dedicada para el BLMOVE bloqueante (ioredis no multiplexa comandos bloqueantes). */
    private readonly blockingRedis: QueueRedis,
    prefix: string,
  ) {
    this.queueKey = `${prefix}:compose:queue`;
    this.processingKey = `${prefix}:compose:processing`;
  }

  async enqueue(job: ComposeJob): Promise<void> {
    await this.redis.lpush(this.queueKey, JSON.stringify(job));
  }

  /** Espera hasta `timeoutSeconds` un trabajo; devuelve el trabajo y su valor crudo (para el ack). */
  async take(timeoutSeconds: number): Promise<{ job: ComposeJob; raw: string } | null> {
    const raw = await this.blockingRedis.blmove(this.queueKey, this.processingKey, 'RIGHT', 'LEFT', timeoutSeconds);
    if (raw === null) return null;
    try {
      return { job: JSON.parse(raw) as ComposeJob, raw };
    } catch {
      await this.redis.lrem(this.processingKey, 1, raw);
      return null;
    }
  }

  async ack(raw: string): Promise<void> {
    await this.redis.lrem(this.processingKey, 1, raw);
  }

  /** Devuelve un trabajo en curso al final de la cola (fallo transitorio al publicar). */
  async requeue(raw: string): Promise<void> {
    await this.redis.lrem(this.processingKey, 1, raw);
    await this.redis.lpush(this.queueKey, raw);
  }

  /** Devuelve a pendientes todo lo que quedó en curso (arranque tras una caída). */
  async recover(): Promise<number> {
    let moved = 0;
    while ((await this.redis.lmove(this.processingKey, this.queueKey, 'RIGHT', 'RIGHT')) !== null) moved += 1;
    return moved;
  }

  async contains(jobId: string): Promise<boolean> {
    for (const key of [this.queueKey, this.processingKey]) {
      const items = await this.redis.lrange(key, 0, -1);
      if (items.some((raw) => raw.includes(`"jobId":"${jobId}"`))) return true;
    }
    return false;
  }

  async depth(): Promise<number> {
    return (await this.redis.llen(this.queueKey)) + (await this.redis.llen(this.processingKey));
  }
}
