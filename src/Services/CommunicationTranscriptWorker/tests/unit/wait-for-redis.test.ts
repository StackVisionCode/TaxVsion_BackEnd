import { EventEmitter } from 'node:events';
import { describe, expect, it, vi } from 'vitest';
import { waitForRedis } from '../../src/redis/wait-for-redis.js';

/**
 * El worker de transcripción moría al arrancar si Redis no respondía: `connect()` de ioredis se rechaza en el primer
 * intento aunque el cliente siga reintentando con su retryStrategy ("fatal startup error", ECONNREFUSED).
 * El arranque tiene que esperar a Redis igual que espera a RabbitMQ.
 */
class FakeRedis extends EventEmitter {
  status = 'wait';
  connect = vi.fn(() => {
    this.status = 'reconnecting';
    return Promise.reject(new Error('connect ECONNREFUSED 127.0.0.1:6379'));
  });
}

describe('waitForRedis', () => {
  it('sigue esperando aunque el primer intento falle y termina cuando Redis queda listo', async () => {
    const client = new FakeRedis();
    let done = false;
    const waiting = waitForRedis(client).then(() => {
      done = true;
    });

    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(client.connect).toHaveBeenCalledTimes(1);
    expect(done).toBe(false);

    client.status = 'ready';
    client.emit('ready');
    await waiting;
    expect(done).toBe(true);
  });

  it('si ya está listo no vuelve a conectar', async () => {
    const client = new FakeRedis();
    client.status = 'ready';

    await waitForRedis(client);

    expect(client.connect).not.toHaveBeenCalled();
  });

  it('si ya está reconectando solo espera, sin pedir otra conexión', async () => {
    const client = new FakeRedis();
    client.status = 'reconnecting';
    const waiting = waitForRedis(client);

    client.emit('ready');
    await waiting;

    expect(client.connect).not.toHaveBeenCalled();
  });
});
