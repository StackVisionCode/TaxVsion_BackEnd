import { randomUUID } from 'node:crypto';
import type { Redis } from 'ioredis';

/**
 * Lock distribuido NX PX + Lua CAS-release. Coordina jobs que corren en
 * setInterval sobre multiples pods (outbox drainer, missed-call scheduler,
 * purge scheduler) para que un solo pod ejecute cada tick — sin esto, N pods
 * hacen trabajo redundante y, en el caso del outbox, publican eventos
 * duplicados (ver docblock de outbox-drainer.ts).
 *
 * El release via Lua evita el bug clasico: si el TTL expira y OTRO pod ya
 * adquirio el lock, no lo borramos por debajo — solo borramos si el token
 * todavia es el nuestro.
 */
const RELEASE_SCRIPT = `
if redis.call("get", KEYS[1]) == ARGV[1] then
  return redis.call("del", KEYS[1])
else
  return 0
end
`;

export class RedisDistributedLock {
  constructor(private readonly redis: Redis) {}

  /**
   * Ejecuta `fn` solo si logra adquirir el lock. Devuelve `undefined` (sin
   * ejecutar `fn`) si otro pod ya lo tiene — el caller debe tratar eso como
   * "skip this tick", no como error.
   */
  async withLock<T>(key: string, ttlMs: number, fn: () => Promise<T>): Promise<T | undefined> {
    const token = randomUUID();
    const acquired = await this.redis.set(key, token, 'PX', ttlMs, 'NX');
    if (acquired !== 'OK') return undefined;
    try {
      return await fn();
    } finally {
      await this.redis.eval(RELEASE_SCRIPT, 1, key, token).catch(() => undefined);
    }
  }
}
