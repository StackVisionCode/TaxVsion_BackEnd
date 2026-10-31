import { beforeAll, describe, expect, it } from 'vitest';
import { RecorderError } from '../../src/contracts/errors.js';
import { createServiceAuthGuard, type ServiceAuthGuard } from '../../src/http/service-auth.js';
import { AUDIENCE, ISSUER, serviceClaims, testKeys, type TestKeys } from './helpers.js';

let keys: TestKeys;
let guard: ServiceAuthGuard;

beforeAll(async () => {
  keys = await testKeys();
  guard = createServiceAuthGuard({ keys: keys.jwks, issuer: ISSUER, audience: AUDIENCE, allowedClientIds: ['communication-worker'] });
});

async function codeOf(p: Promise<unknown>): Promise<string> {
  try {
    await p;
    return 'ok';
  } catch (err) {
    expect(err).toBeInstanceOf(RecorderError);
    return `${(err as RecorderError).statusCode} ${(err as RecorderError).code}`;
  }
}

describe('service auth guard (JWKS local)', () => {
  it('acepta un JWT de servicio permitido', async () => {
    const token = await keys.sign(serviceClaims());
    const principal = await guard(`Bearer ${token}`);
    expect(principal.clientId).toBe('communication-worker');
    expect(principal.tenantId).toBe('00000000-0000-0000-0000-000000000001');
  });

  it('sin token, mal formado, firma ajena, expirado, issuer o audience distintos → 401', async () => {
    const other = await testKeys();
    expect(await codeOf(guard(undefined))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard('Basic abc'))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard('Bearer not-a-jwt'))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard(`Bearer ${await other.sign(serviceClaims())}`))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard(`Bearer ${await keys.sign(serviceClaims(), { expired: true })}`))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard(`Bearer ${await keys.sign(serviceClaims(), { issuer: 'Other' })}`))).toBe('401 Auth.InvalidToken');
    expect(await codeOf(guard(`Bearer ${await keys.sign(serviceClaims(), { audience: 'Other' })}`))).toBe('401 Auth.InvalidToken');
  });

  it('token de usuario o client_id no permitido → 403', async () => {
    const user = await keys.sign({ sub: 'u1', actor_type: 'Staff', tenant_id: 't' });
    expect(await codeOf(guard(`Bearer ${user}`))).toBe('403 Auth.ClientNotAllowed');
    const otherService = await keys.sign(serviceClaims('documents-worker'));
    expect(await codeOf(guard(`Bearer ${otherService}`))).toBe('403 Auth.ClientNotAllowed');
    const noClient = await keys.sign({ sub: 'svc', actor_type: 'Service' });
    expect(await codeOf(guard(`Bearer ${noClient}`))).toBe('403 Auth.ClientNotAllowed');
  });
});
