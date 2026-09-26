import Fastify from 'fastify';
import { describe, expect, it, vi } from 'vitest';
import { registerAuthPlugin, USER_HTTP_RATE_LIMIT_POLICY } from '../../src/api/http/plugins/auth.plugin.js';
import type { HttpRateLimitDecision, HttpRateLimiter } from '../../src/infrastructure/redis/http-rate-limiter.js';

vi.mock('../../src/infrastructure/jwks/jwt-verifier.js', () => ({
  UnauthorizedError: class UnauthorizedError extends Error {
    code = 'Auth.InvalidToken';
  },
  verifyAccessToken: async (token: string) => {
    if (token !== 'valid') throw new Error('invalid');
    return { userId: 'user-1', tenantId: 'tenant-1', actorType: 'TenantEmployee', permissions: [], permissionVersion: 1 };
  },
}));

function fakeLimiter(decision: HttpRateLimitDecision) {
  const keys: string[] = [];
  const limiter = {
    allow: async (input: { key: string }) => {
      keys.push(input.key);
      return decision;
    },
  } as unknown as HttpRateLimiter;
  return { limiter, keys };
}

async function buildApp(limiter: HttpRateLimiter) {
  const app = Fastify();
  await app.register(registerAuthPlugin, { httpRateLimiter: limiter, userRateLimit: { maxPerWindow: 600, windowSeconds: 60 } });
  app.get('/private', { preHandler: [app.authenticate] }, async () => ({ ok: true }));
  await app.ready();
  return app;
}

/**
 * La cuota HTTP por usuario se aplica recien con el JWT verificado: con el `sub` sin verificar,
 * cualquiera podria forjarlo y agotarle el cupo a otro usuario.
 */
describe('authenticate — per-user HTTP rate limit', () => {
  it('counts the verified user (tenant + user) and lets the request through within quota', async () => {
    const { limiter, keys } = fakeLimiter({ allowed: true, retryAfterSeconds: 0 });
    const app = await buildApp(limiter);

    const response = await app.inject({ method: 'GET', url: '/private', headers: { authorization: 'Bearer valid' } });

    expect(response.statusCode).toBe(200);
    expect(keys).toEqual(['comm:rl:http.user:tenant-1:user-1']);
  });

  it('answers the shared 429 contract once the user runs out of quota', async () => {
    const { limiter } = fakeLimiter({ allowed: false, retryAfterSeconds: 17 });
    const app = await buildApp(limiter);

    const response = await app.inject({ method: 'GET', url: '/private', headers: { authorization: 'Bearer valid' } });

    expect(response.statusCode).toBe(429);
    expect(response.headers['retry-after']).toBe('17');
    expect(response.json()).toMatchObject({ code: 'RateLimit.Exceeded', retryAfterSeconds: 17, policy: USER_HTTP_RATE_LIMIT_POLICY });
  });

  it('does not count anything when the token is invalid', async () => {
    const { limiter, keys } = fakeLimiter({ allowed: true, retryAfterSeconds: 0 });
    const app = await buildApp(limiter);

    const response = await app.inject({ method: 'GET', url: '/private', headers: { authorization: 'Bearer forged' } });

    expect(response.statusCode).toBe(401);
    expect(keys).toEqual([]);
  });
});
