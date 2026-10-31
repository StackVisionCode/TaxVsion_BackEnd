import { createRemoteJWKSet, jwtVerify, type JWTPayload, type JWTVerifyGetKey } from 'jose';
import { Errors } from '../contracts/errors.js';

export interface ServicePrincipal {
  readonly clientId: string;
  readonly tenantId: string | undefined;
  readonly raw: JWTPayload;
}

export type ServiceAuthGuard = (authorization: string | undefined) => Promise<ServicePrincipal>;

/**
 * Canal de control solo para servicios: JWT de Auth verificado contra su JWKS (iss/aud/algoritmo),
 * `actor_type = Service` y `client_id` en la lista permitida. Sin token válido → 401; servicio no
 * permitido (o token de usuario) → 403.
 */
export function createServiceAuthGuard(opts: {
  keys: JWTVerifyGetKey;
  issuer: string;
  audience: string;
  allowedClientIds: readonly string[];
}): ServiceAuthGuard {
  const allowed = new Set(opts.allowedClientIds);
  return async (authorization) => {
    const match = /^Bearer\s+(.+)$/i.exec(authorization ?? '');
    const token = match?.[1]?.trim();
    if (!token) throw Errors.invalidToken();
    let payload: JWTPayload;
    try {
      ({ payload } = await jwtVerify(token, opts.keys, {
        issuer: opts.issuer,
        audience: opts.audience,
        algorithms: ['RS256', 'PS256', 'ES256'],
      }));
    } catch {
      throw Errors.invalidToken();
    }
    const actorType = payload['actor_type'];
    const clientId = payload['client_id'];
    if (actorType !== 'Service' || typeof clientId !== 'string' || !allowed.has(clientId)) {
      throw Errors.clientNotAllowed();
    }
    const tenantId = typeof payload['tenant_id'] === 'string' ? payload['tenant_id'] : undefined;
    return { clientId, tenantId, raw: payload };
  };
}

export function remoteJwks(jwksUri: string, cacheMaxAgeSeconds: number): JWTVerifyGetKey {
  return createRemoteJWKSet(new URL(jwksUri), {
    cacheMaxAge: cacheMaxAgeSeconds * 1000,
    cooldownDuration: 10_000,
  });
}
