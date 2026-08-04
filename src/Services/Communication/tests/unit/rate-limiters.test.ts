import { describe, expect, it } from 'vitest';
import type { Redis } from 'ioredis';
import { HttpRateLimiter } from '../../src/infrastructure/redis/http-rate-limiter.js';
import { SocketRateLimiter } from '../../src/infrastructure/redis/socket-rate-limiter.js';

function fakeRedisCountingUp(): Redis {
  let count = 0;
  return {
    eval: async () => {
      count += 1;
      return count;
    },
  } as unknown as Redis;
}

function fakeRedisThatThrows(): Redis {
  return {
    eval: async () => {
      throw new Error('Redis connection refused.');
    },
  } as unknown as Redis;
}

describe('HttpRateLimiter.allow', () => {
  it('allows requests within the window and blocks once the limit is exceeded', async () => {
    const limiter = new HttpRateLimiter(fakeRedisCountingUp());
    const input = { key: 'comm:rl:http.global:1.2.3.4', policy: 'communication.global_http_ip', maxPerWindow: 2, windowSeconds: 60 };

    await expect(limiter.allow(input)).resolves.toBe(true);
    await expect(limiter.allow(input)).resolves.toBe(true);
    await expect(limiter.allow(input)).resolves.toBe(false);
  });

  // Auditoria RateLimit hallazgo #3 — antes de esto, un Redis caido relanzaba la excepcion (fail
  // CLOSED pese al nombre de la metrica fallback_open_total). Debe fail-open igual que el lado
  // .NET (TieredRateLimitEvaluator) y el ADR_017.
  it('fails open and allows the request when Redis throws', async () => {
    const limiter = new HttpRateLimiter(fakeRedisThatThrows());

    await expect(
      limiter.allow({ key: 'comm:rl:http.global:1.2.3.4', policy: 'communication.global_http_ip', maxPerWindow: 1, windowSeconds: 60 }),
    ).resolves.toBe(true);
  });
});

describe('SocketRateLimiter.allow', () => {
  it('allows events within the window and blocks once the limit is exceeded', async () => {
    const limiter = new SocketRateLimiter(fakeRedisCountingUp());
    const input = { scope: 'chat.send_message', tenantId: 'tenant-1', userId: 'user-1', maxPerWindow: 1, windowSeconds: 60 };

    await expect(limiter.allow(input)).resolves.toBe(true);
    await expect(limiter.allow(input)).resolves.toBe(false);
  });

  it('fails open and allows the event when Redis throws', async () => {
    const limiter = new SocketRateLimiter(fakeRedisThatThrows());

    await expect(
      limiter.allow({ scope: 'chat.send_message', tenantId: 'tenant-1', userId: 'user-1', maxPerWindow: 1, windowSeconds: 60 }),
    ).resolves.toBe(true);
  });
});
