import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';
import type { TenantDirectoryZoneRow } from '../schedulers/tenant-time-zone-reconciliation-scheduler.js';

// Sin URL configurada se usa el Tenant local, igual que el kit .NET; el compose de producción la define.
const LOCAL_TENANT_BASE_URL = 'http://localhost:5217';

/**
 * Lee `GET internal/tenants/directory?page={n}&size={n}` de Tenant (ServiceOnly, token de la PlatformTenant):
 * la zona actual de cada oficina para la reconciliación. Fail-soft: cualquier fallo devuelve null.
 */
export class HttpTenantDirectoryClient {
  private readonly baseUrl: string;

  constructor(
    private readonly tokens: ServiceTokenClient,
    tenantBaseUrl: string | undefined = config.platform.tenantBaseUrl,
  ) {
    this.baseUrl = (tenantBaseUrl || LOCAL_TENANT_BASE_URL).replace(/\/+$/, '');
  }

  async listPage(page: number, size: number): Promise<TenantDirectoryZoneRow[] | null> {
    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'tenant directory: could not acquire service token; skipping');
      return null;
    }

    try {
      const url = `${this.baseUrl}/internal/tenants/directory?page=${page}&size=${size}`;
      const response = await fetch(url, { headers: { authorization: `Bearer ${token}` } });
      if (!response.ok) {
        logger.warn({ status: response.status, page }, 'tenant directory: request failed; skipping');
        return null;
      }
      const raw = (await response.json()) as unknown;
      if (!Array.isArray(raw)) return null;
      return (raw as Readonly<Record<string, unknown>>[]).map((item) => ({
        tenantId: String(item['id'] ?? item['Id'] ?? ''),
        timeZoneId: String(item['defaultTimeZoneId'] ?? item['DefaultTimeZoneId'] ?? ''),
      }));
    } catch (error) {
      logger.warn({ error: (error as Error).message, page }, 'tenant directory: request threw; skipping');
      return null;
    }
  }
}
