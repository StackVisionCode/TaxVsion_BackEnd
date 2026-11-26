import { beforeEach, describe, expect, it, vi } from 'vitest';

const payload: Record<string, unknown> = {};

/**
 * Redis de mentira con la semántica real: Auth escribe la denylist con IDistributedCache, que guarda un
 * hash. MGET devuelve null para claves que no son string; EXISTS cuenta cualquier tipo.
 */
const keys = new Map<string, 'hash' | 'string'>();
let redisDown = false;

vi.mock('jose', () => ({
  createRemoteJWKSet: () => async () => ({}),
  jwtVerify: async () => ({ payload }),
}));

vi.mock('../../src/infrastructure/redis/redis-client.js', () => ({
  redis: {
    mget: async (...ks: string[]) => {
      if (redisDown) throw new Error('connect ECONNREFUSED');
      return ks.map((k) => (keys.get(k) === 'string' ? '1' : null));
    },
    exists: async (...ks: string[]) => {
      if (redisDown) throw new Error('connect ECONNREFUSED');
      return ks.filter((k) => keys.has(k)).length;
    },
  },
}));

const { verifyAccessToken } = await import('../../src/infrastructure/jwks/jwt-verifier.js');

const SID = '11111111-2222-3333-4444-555555555555';
const SID_KEY = `taxvision:auth:denylist:sid:${SID.replace(/-/g, '')}`;

beforeEach(() => {
  keys.clear();
  redisDown = false;
  for (const key of Object.keys(payload)) delete payload[key];
  Object.assign(payload, {
    sub: 'user-1',
    tenant_id: 'tenant-1',
    actor_type: 'TenantEmployee',
    sid: SID,
    jti: 'jti-1',
  });
});

describe('verifyAccessToken y la denylist de sesiones', () => {
  it('rechaza una sesión revocada por Auth (la clave es un hash)', async () => {
    keys.set(SID_KEY, 'hash');

    await expect(verifyAccessToken('token')).rejects.toMatchObject({ code: 'Auth.SessionRevoked' });
  });

  it('también rechaza si la clave es un string', async () => {
    keys.set(SID_KEY, 'string');

    await expect(verifyAccessToken('token')).rejects.toMatchObject({ code: 'Auth.SessionRevoked' });
  });

  it('acepta una sesión que no está en la denylist', async () => {
    const principal = await verifyAccessToken('token');

    expect(principal.sessionId).toBe(SID);
  });

  it('con Redis caído se comporta como hoy: rechaza por no poder comprobarlo', async () => {
    redisDown = true;

    await expect(verifyAccessToken('token')).rejects.toMatchObject({ code: 'Auth.DenylistUnavailable' });
  });
});
