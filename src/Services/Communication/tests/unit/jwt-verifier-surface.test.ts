import { describe, expect, it, vi } from 'vitest';

const payload: Record<string, unknown> = {};

vi.mock('jose', () => ({
  createRemoteJWKSet: () => async () => ({}),
  jwtVerify: async () => ({ payload }),
}));

vi.mock('../../src/infrastructure/redis/redis-client.js', () => ({
  redis: { mget: async () => [null, null] },
}));

const { verifyAccessToken, UnauthorizedError } = await import(
  '../../src/infrastructure/jwks/jwt-verifier.js'
);

function claims(extra: Record<string, unknown> = {}): void {
  for (const key of Object.keys(payload)) delete payload[key];
  Object.assign(payload, {
    sub: 'user-1',
    tenant_id: 'tenant-1',
    actor_type: 'TenantAdmin',
    sid: '11111111-1111-1111-1111-111111111111',
    jti: 'jti-1',
    ...extra,
  });
}

/**
 * Un token emitido para una superficie acotada solo sirve donde esa superficie se declara. Communication no
 * tiene nada del Account, y su verificador es de Node: el filtro de MVC que protege a los .NET no llega aca.
 */
describe('verifyAccessToken y la superficie del token', () => {
  it('rechaza un token del Account', async () => {
    claims({ surface: 'account' });

    await expect(verifyAccessToken('token')).rejects.toMatchObject({
      code: 'Auth.SurfaceNotAllowed',
    });
  });

  it('rechaza cualquier superficie, no solo la que existe hoy', async () => {
    claims({ surface: 'alguna-superficie-futura' });

    await expect(verifyAccessToken('token')).rejects.toBeInstanceOf(UnauthorizedError);
  });

  it('acepta el token del workspace, que no lleva superficie', async () => {
    claims();

    const principal = await verifyAccessToken('token');

    expect(principal.userId).toBe('user-1');
    expect(principal.actorType).toBe('TenantAdmin');
  });
});
