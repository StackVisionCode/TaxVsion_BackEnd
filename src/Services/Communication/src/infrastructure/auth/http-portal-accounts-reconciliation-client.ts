import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from './service-token-client.js';

/**
 * Lector cross-tenant de las cuentas de portal de Auth para el reconciliador de CustomerPortalAccount.
 * Llama al M2M `GET internal/users/portal-accounts/reconciliation?page={n}&size={n}` (ServiceOnly, solo
 * token de servicio del PlatformTenant), el mismo que ya consume Notification. Mismo patron y
 * credencial que HttpUserDirectoryReconciliationClient. Fail-soft: cualquier fallo devuelve null.
 */
export interface PortalAccountReconciliationRow {
  readonly tenantId: string;
  readonly customerId: string;
  readonly userId: string;
  readonly isActive: boolean;
}

export interface PortalAccountReconciliationPage {
  readonly items: PortalAccountReconciliationRow[];
  readonly hasMore: boolean;
}

type RawRow = Readonly<Record<string, unknown>>;

export class HttpPortalAccountsReconciliationClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async listPage(page: number, size: number): Promise<PortalAccountReconciliationPage | null> {
    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'could not acquire service token for portal account reconciliation; skipping');
      return null;
    }

    let response: Response;
    try {
      const url = `${config.serviceAuth.authBaseUrl}/internal/users/portal-accounts/reconciliation?page=${page}&size=${size}`;
      response = await fetch(url, { headers: { authorization: `Bearer ${token}` } });
    } catch (error) {
      logger.warn({ error, page }, 'portal account reconciliation request failed; skipping');
      return null;
    }

    if (!response.ok) {
      logger.warn(
        { status: response.status, page },
        'portal account reconciliation request failed; skipping',
      );
      return null;
    }

    let raw: RawRow;
    try {
      raw = (await response.json()) as RawRow;
    } catch (error) {
      logger.warn({ error, page }, 'portal account reconciliation response was not valid JSON; skipping');
      return null;
    }

    const rawItems = pick(raw, 'items', 'Items');
    if (!Array.isArray(rawItems)) return null;

    const items: PortalAccountReconciliationRow[] = (rawItems as RawRow[]).map((entry) => ({
      tenantId: pickString(entry, 'tenantId', 'TenantId') ?? '',
      customerId: pickString(entry, 'customerId', 'CustomerId') ?? '',
      userId: pickString(entry, 'userId', 'UserId') ?? '',
      isActive: pick(entry, 'isActive', 'IsActive') === true,
    }));

    const size_ = pickNumber(raw, 'size', 'Size') ?? size;
    const page_ = pickNumber(raw, 'page', 'Page') ?? page;
    const totalCount = pickNumber(raw, 'totalCount', 'TotalCount') ?? 0;
    return { items, hasMore: page_ * size_ < totalCount };
  }
}

function pick(source: RawRow, camel: string, pascal: string): unknown {
  return source[camel] !== undefined ? source[camel] : source[pascal];
}

function pickString(source: RawRow, camel: string, pascal: string): string | undefined {
  const value = pick(source, camel, pascal);
  return typeof value === 'string' ? value : undefined;
}

function pickNumber(source: RawRow, camel: string, pascal: string): number | undefined {
  const value = pick(source, camel, pascal);
  return typeof value === 'number' ? value : undefined;
}
