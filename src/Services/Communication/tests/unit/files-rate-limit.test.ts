import Fastify from 'fastify';
import { describe, expect, it, vi } from 'vitest';
import {
  FILES_HTTP_RATE_LIMIT_POLICY,
  registerAuthPlugin,
  USER_HTTP_RATE_LIMIT_POLICY,
} from '../../src/api/http/plugins/auth.plugin.js';
import { globalHttpRateLimitApplies } from '../../src/infrastructure/http/global-http-rate-limit.js';
import type { HttpRateLimiter } from '../../src/infrastructure/redis/http-rate-limiter.js';

vi.mock('../../src/infrastructure/jwks/jwt-verifier.js', () => ({
  UnauthorizedError: class UnauthorizedError extends Error {
    code = 'Auth.InvalidToken';
  },
  verifyAccessToken: async () => ({
    userId: 'user-1',
    tenantId: 'tenant-1',
    actorType: 'TenantEmployee',
    permissions: [],
    permissionVersion: 1,
  }),
}));

async function buildApp() {
  const calls: { key: string; policy: string }[] = [];
  const limiter = {
    allow: async (input: { key: string; policy: string }) => {
      calls.push({ key: input.key, policy: input.policy });
      return { allowed: true, retryAfterSeconds: 0 };
    },
  } as unknown as HttpRateLimiter;
  const app = Fastify();
  await app.register(registerAuthPlugin, {
    httpRateLimiter: limiter,
    userRateLimit: { maxPerWindow: 600, windowSeconds: 60 },
  });
  app.get('/communication/files/scope', { preHandler: [app.authenticate] }, async () => ({ ok: true }));
  app.post('/communication/files/visibility', { preHandler: [app.authenticate] }, async () => ({ ok: true }));
  app.get('/communication/conversations', { preHandler: [app.authenticate] }, async () => ({ ok: true }));
  await app.ready();
  return { app, calls };
}

/**
 * La biblioteca llama a `/communication/files/*` hasta 5 veces por búsqueda con el token de la persona: no puede
 * gastar la cuota con la que esa persona usa el chat. Las rutas de archivos tienen su propio cupo por usuario.
 */
describe('cuota HTTP de /communication/files/*', () => {
  it('las rutas de archivos cuentan en su propio cupo, no en el general del usuario', async () => {
    const { app, calls } = await buildApp();

    await app.inject({
      method: 'GET',
      url: '/communication/files/scope',
      headers: { authorization: 'Bearer x' },
    });
    await app.inject({
      method: 'POST',
      url: '/communication/files/visibility',
      headers: { authorization: 'Bearer x' },
      payload: {},
    });
    await app.inject({
      method: 'GET',
      url: '/communication/conversations',
      headers: { authorization: 'Bearer x' },
    });

    expect(calls).toEqual([
      { key: 'comm:rl:http.files:tenant-1:user-1', policy: FILES_HTTP_RATE_LIMIT_POLICY },
      { key: 'comm:rl:http.files:tenant-1:user-1', policy: FILES_HTTP_RATE_LIMIT_POLICY },
      { key: 'comm:rl:http.user:tenant-1:user-1', policy: USER_HTTP_RATE_LIMIT_POLICY },
    ]);
  });

  it('el límite por IP no se aplica a la llamada interna de CloudStorage (sin CF-Connecting-IP), sí al resto', () => {
    expect(globalHttpRateLimitApplies('/communication/files/scope', undefined)).toBe(false);
    expect(globalHttpRateLimitApplies('/communication/files/visibility', undefined)).toBe(false);
    expect(globalHttpRateLimitApplies('/communication/files/scope', '203.0.113.7')).toBe(true);
    expect(globalHttpRateLimitApplies('/communication/conversations', undefined)).toBe(true);
    expect(globalHttpRateLimitApplies('/communication/files-other', undefined)).toBe(true);
  });
});
