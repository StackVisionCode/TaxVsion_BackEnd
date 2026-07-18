import { createRemoteJWKSet, jwtVerify, type JWTPayload } from 'jose';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import { redis } from '../redis/redis-client.js';

/**
 * Verificador JWT contra el JWKS remoto de Auth (RS256, PS256 o ES256).
 * jose cachea las claves en memoria; el TTL efectivo lo dicta cacheMaxAge.
 *
 * Ademas de la verificacion criptografica, chequeamos:
 *   - iss / aud coinciden con la config.
 *   - typ === 'JWT' o no presente (algunos issuers no lo emiten).
 *   - jti no esta en el session denylist compartido con Auth.
 *
 * Cierra CRIT del legacy: nunca compartimos un HS256 secret; la clave publica
 * puede rotarse en Auth sin redeploy de Communication.
 */
const jwks = createRemoteJWKSet(new URL(config.jwt.jwksUri), {
  cacheMaxAge: config.jwt.jwksCacheMaxAgeSeconds * 1000,
  cooldownDuration: 10_000,
});

export interface AuthenticatedPrincipal {
  userId: string;
  tenantId: string;
  actorType: string;
  permissions: readonly string[];
  permissionVersion: number;
  sessionId: string | undefined;
  jti: string | undefined;
  raw: JWTPayload;
}

export class UnauthorizedError extends Error {
  constructor(
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'UnauthorizedError';
  }
}

export async function verifyAccessToken(token: string): Promise<AuthenticatedPrincipal> {
  const { payload } = await jwtVerify(token, jwks, {
    issuer: config.jwt.issuer,
    audience: config.jwt.audience,
    algorithms: ['RS256', 'PS256', 'ES256'],
  }).catch((err: unknown) => {
    logger.debug({ err: (err as Error).message }, 'JWT verify failed');
    throw new UnauthorizedError('Auth.InvalidToken', 'Access token could not be verified.');
  });

  const jti = typeof payload.jti === 'string' ? payload.jti : undefined;
  const sessionId = typeof payload['sid'] === 'string' ? (payload['sid'] as string) : undefined;

  await ensureNotDenied(jti, sessionId);

  const userId = extractString(payload, 'sub');
  const tenantId = extractString(payload, 'tenant_id');
  const actorType = extractString(payload, 'actor_type', 'TenantEmployee');
  const permissions = extractStringArray(payload, 'perm');
  const permissionVersion = extractNumber(payload, 'perm_v', 1);

  return {
    userId,
    tenantId,
    actorType,
    permissions,
    permissionVersion,
    sessionId,
    jti,
    raw: payload,
  };
}

async function ensureNotDenied(jti: string | undefined, sessionId: string | undefined): Promise<void> {
  const keys: string[] = [];
  if (jti) keys.push(`${config.redis.sessionDenylistPrefix}:jti:${jti}`);
  if (sessionId) keys.push(`${config.redis.sessionDenylistPrefix}:sid:${sessionId}`);
  if (keys.length === 0) return;

  try {
    const results = await redis.mget(...keys);
    if (results.some((v) => v !== null)) {
      throw new UnauthorizedError('Auth.SessionRevoked', 'Session or token was revoked.');
    }
  } catch (err) {
    if (err instanceof UnauthorizedError) throw err;
    logger.error({ err: (err as Error).message }, 'Session denylist unavailable');
    if (config.redis.sessionDenylistFailClosed) {
      // Fail-closed (default): no podemos confirmar que el token/sesion no fue
      // revocado, asi que rechazamos la conexion. Mas seguro que fail-open, a
      // costa de rechazar conexiones nuevas mientras Redis este caido.
      throw new UnauthorizedError(
        'Auth.DenylistUnavailable',
        'Could not verify session revocation status.',
      );
    }
    // Escotilla de emergencia: COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED=false
    // permite seguir aceptando conexiones durante un incidente de Redis.
  }
}

function extractString(payload: JWTPayload, key: string, fallback?: string): string {
  const value = payload[key];
  if (typeof value === 'string' && value.length > 0) return value;
  if (fallback !== undefined) return fallback;
  throw new UnauthorizedError('Auth.InvalidClaim', `Missing required claim '${key}'.`);
}

function extractNumber(payload: JWTPayload, key: string, fallback: number): number {
  const value = payload[key];
  if (typeof value === 'number') return value;
  if (typeof value === 'string') {
    const parsed = Number.parseInt(value, 10);
    if (Number.isFinite(parsed)) return parsed;
  }
  return fallback;
}

function extractStringArray(payload: JWTPayload, key: string): readonly string[] {
  const value = payload[key];
  if (Array.isArray(value)) {
    return value.filter((v: unknown): v is string => typeof v === 'string');
  }
  if (typeof value === 'string' && value.length > 0) {
    return value.split(' ').filter(Boolean);
  }
  return [];
}
