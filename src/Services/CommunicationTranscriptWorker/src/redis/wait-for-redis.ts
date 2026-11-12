import type { EventEmitter } from 'node:events';

/** Lo mínimo de un cliente ioredis para esperarlo. */
export interface RedisLike extends Pick<EventEmitter, 'once'> {
  readonly status: string;
  connect(): Promise<void>;
}

/**
 * Espera a Redis sin límite, igual que el arranque espera a RabbitMQ. Si Redis no responde, el primer
 * `connect()` de ioredis se rechaza pero el cliente sigue reintentando con su retryStrategy: se ignora ese
 * rechazo (el evento 'error' ya lo registra) y se espera `ready`.
 */
export function waitForRedis(client: RedisLike): Promise<void> {
  if (client.status === 'ready') return Promise.resolve();
  const ready = new Promise<void>((resolve) => {
    client.once('ready', () => resolve());
  });
  if (client.status === 'wait') {
    client.connect().catch(() => undefined);
  }
  return ready;
}
