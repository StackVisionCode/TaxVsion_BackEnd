import { describe, expect, it } from 'vitest';
import type { Redis } from 'ioredis';
import { HttpRateLimiter } from '../../src/infrastructure/redis/http-rate-limiter.js';
import { SocketRateLimiter } from '../../src/infrastructure/redis/socket-rate-limiter.js';

/**
 * Emula CONSUME_WITHIN_LIMIT_SCRIPT: consume solo si hay cupo (un rechazo no incrementa) y devuelve
 * [permitido, ttl_ms]. Registra los argumentos para verificar que el limiter le pasa el máximo.
 */
function fakeRedisWithLimit(ttlMs = 42_000) {
  let count = 0;
  const calls: unknown[][] = [];
  const redis = {
    eval: async (...args: unknown[]) => {
      calls.push(args);
      const max = Number(args[4]);
      if (count >= max) return [0, ttlMs];
      count += 1;
      return [1, 0];
    },
  } as unknown as Redis;
  return { redis, calls, consumed: () => count };
}

function fakeRedisThatThrows(): Redis {
  return {
    eval: async () => {
      throw new Error('Redis connection refused.');
    },
  } as unknown as Redis;
}

describe('HttpRateLimiter.allow', () => {
  const input = { key: 'comm:rl:http.global:1.2.3.4', policy: 'communication.global_http_ip', maxPerWindow: 2, windowSeconds: 60 };

  it('allows requests within the window and blocks once the limit is reached', async () => {
    const limiter = new HttpRateLimiter(fakeRedisWithLimit().redis);

    await expect(limiter.allow(input)).resolves.toEqual({ allowed: true, retryAfterSeconds: 0 });
    await expect(limiter.allow(input)).resolves.toEqual({ allowed: true, retryAfterSeconds: 0 });
    expect((await limiter.allow(input)).allowed).toBe(false);
  });

  it('passes the limit to the script so a rejection does not consume quota', async () => {
    const fake = fakeRedisWithLimit();
    const limiter = new HttpRateLimiter(fake.redis);

    for (let i = 0; i < 5; i++) await limiter.allow(input);

    expect(fake.calls[0]).toEqual([expect.any(String), 1, input.key, input.windowSeconds, input.maxPerWindow]);
    expect(fake.consumed()).toBe(2);
  });

  it('reports the real time left in the window, not the full window', async () => {
    const limiter = new HttpRateLimiter(fakeRedisWithLimit(12_300).redis);
    await limiter.allow(input);
    await limiter.allow(input);

    await expect(limiter.allow(input)).resolves.toEqual({ allowed: false, retryAfterSeconds: 13 });
  });

  it('falls back to the full window when the key has no TTL', async () => {
    const limiter = new HttpRateLimiter(fakeRedisWithLimit(-1).redis);
    await limiter.allow(input);
    await limiter.allow(input);

    await expect(limiter.allow(input)).resolves.toEqual({ allowed: false, retryAfterSeconds: 60 });
  });

  // Auditoria RateLimit hallazgo #3 — antes de esto, un Redis caido relanzaba la excepcion (fail
  // CLOSED pese al nombre de la metrica fallback_open_total). Debe fail-open igual que el lado
  // .NET (TieredRateLimitEvaluator) y el ADR_017.
  it('fails open and allows the request when Redis throws', async () => {
    const limiter = new HttpRateLimiter(fakeRedisThatThrows());

    await expect(limiter.allow({ ...input, maxPerWindow: 1 })).resolves.toEqual({ allowed: true, retryAfterSeconds: 0 });
  });
});

describe('SocketRateLimiter.allow', () => {
  const input = { scope: 'chat.send_message', tenantId: 'tenant-1', userId: 'user-1', maxPerWindow: 1, windowSeconds: 60 };

  it('allows events within the window and blocks once the limit is reached', async () => {
    const limiter = new SocketRateLimiter(fakeRedisWithLimit().redis);

    await expect(limiter.allow(input)).resolves.toBe(true);
    await expect(limiter.allow(input)).resolves.toBe(false);
  });

  it('keys the counter by scope, tenant and user', async () => {
    const fake = fakeRedisWithLimit();
    const limiter = new SocketRateLimiter(fake.redis);

    await limiter.allow(input);

    expect(fake.calls[0]?.[2]).toBe('comm:rl:chat.send_message:tenant-1:user-1');
  });

  it('fails open and allows the event when Redis throws', async () => {
    const limiter = new SocketRateLimiter(fakeRedisThatThrows());

    await expect(limiter.allow(input)).resolves.toBe(true);
  });
});
