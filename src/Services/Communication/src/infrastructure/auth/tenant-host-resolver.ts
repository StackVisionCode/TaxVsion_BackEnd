import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { TenantHostResolver } from '../../application/ports/tenant-host-resolver.js';
import type { ServiceTokenClient } from './service-token-client.js';

/**
 * Pull M2M del host primario de un tenant contra Auth (`GET internal/tenants/{tenantId}/primary-host`,
 * policy ServiceOnly). Reusa el {@link ServiceTokenClient} que ya cablea este servicio (mismo
 * client-id/secret M2M). Espejo Node del `TenantHostResolver` de Notification: cachea en memoria con
 * TTL corto (los subdominios casi nunca cambian) y NUNCA lanza — cualquier fallo devuelve `null` y el
 * caller cae al base fijo.
 */
const CACHE_TTL_MS = 15 * 60 * 1000;

interface CachedHost {
  readonly host: string;
  readonly expiresAtMs: number;
}

interface PrimaryHostResponse {
  readonly host: string;
}

export class HttpTenantHostResolver implements TenantHostResolver {
  private readonly cache = new Map<string, CachedHost>();

  constructor(private readonly serviceTokens: ServiceTokenClient) {}

  async resolveHost(tenantId: string): Promise<string | null> {
    const cached = this.cache.get(tenantId);
    if (cached && cached.expiresAtMs > Date.now()) {
      return cached.host;
    }

    const host = await this.fetchHost(tenantId);
    if (host) {
      this.cache.set(tenantId, { host, expiresAtMs: Date.now() + CACHE_TTL_MS });
    }
    return host;
  }

  private async fetchHost(tenantId: string): Promise<string | null> {
    let token: string;
    try {
      token = await this.serviceTokens.getToken(tenantId);
    } catch (err) {
      logger.warn({ err: (err as Error).message, tenantId }, 'primary-host: service-token failed');
      return null;
    }

    try {
      const response = await fetch(`${config.serviceAuth.authBaseUrl}/internal/tenants/${tenantId}/primary-host`, {
        headers: { authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        logger.info({ status: response.status, tenantId }, 'primary-host: pull returned non-2xx');
        return null;
      }
      const dto = (await response.json()) as PrimaryHostResponse;
      const host = dto?.host?.trim();
      return host ? host : null;
    } catch (err) {
      logger.warn({ err: (err as Error).message, tenantId }, 'primary-host: pull threw');
      return null;
    }
  }
}
