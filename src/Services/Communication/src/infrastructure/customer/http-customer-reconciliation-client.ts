import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * Lector cross-tenant del directorio de customers para el reconciliador
 * periodico (Fase Backend — directory reconciliation). Llama al endpoint M2M
 * `GET internal/customers/reconciliation?status=All&page={n}&size={n}` de
 * Customer.Api (ServiceOnly), que solo acepta un service token cuyo tenant_id
 * == el PlatformTenant id. Igual que HttpPlanRateLimitReader, adquiere el token
 * con `config.platformTenantId` como sentinel — una sola credencial M2M cubre
 * la lectura global de todos los tenants.
 *
 * Fail-soft total: cualquier fallo (token, red, status, parseo) devuelve null y
 * el scheduler corta la pasada sin lanzar — nunca revienta un tick.
 */
export interface CustomerReconciliationRow {
  readonly tenantId: string;
  readonly customerId: string;
  readonly displayName: string;
  readonly primaryEmail: string;
  readonly status: string;
}

export interface CustomerReconciliationPage {
  readonly items: CustomerReconciliationRow[];
  readonly hasMore: boolean;
}

interface RawReconciliationRow {
  readonly tenantId?: unknown;
  readonly TenantId?: unknown;
  readonly customerId?: unknown;
  readonly CustomerId?: unknown;
  readonly displayName?: unknown;
  readonly DisplayName?: unknown;
  readonly primaryEmail?: unknown;
  readonly PrimaryEmail?: unknown;
  readonly status?: unknown;
  readonly Status?: unknown;
}

interface RawReconciliationResponse {
  readonly items?: unknown;
  readonly Items?: unknown;
  readonly page?: unknown;
  readonly Page?: unknown;
  readonly size?: unknown;
  readonly Size?: unknown;
  readonly totalCount?: unknown;
  readonly TotalCount?: unknown;
}

export class HttpCustomerReconciliationClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async listPage(page: number, size: number): Promise<CustomerReconciliationPage | null> {
    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'could not acquire service token for customer reconciliation; skipping');
      return null;
    }

    let response: Response;
    try {
      const url = `${config.customer.baseUrl}/internal/customers/reconciliation?status=All&page=${page}&size=${size}`;
      response = await fetch(url, { headers: { authorization: `Bearer ${token}` } });
    } catch (error) {
      logger.warn({ error, page }, 'customer reconciliation request failed; skipping');
      return null;
    }

    if (!response.ok) {
      logger.warn({ status: response.status, page }, 'customer reconciliation request failed; skipping');
      return null;
    }

    let raw: RawReconciliationResponse;
    try {
      raw = (await response.json()) as RawReconciliationResponse;
    } catch (error) {
      logger.warn({ error, page }, 'customer reconciliation response was not valid JSON; skipping');
      return null;
    }

    const rawItems = pick(raw.items, raw.Items);
    if (!Array.isArray(rawItems)) return null;

    const items: CustomerReconciliationRow[] = [];
    for (const entry of rawItems as RawReconciliationRow[]) {
      const tenantId = pickString(entry.tenantId, entry.TenantId);
      const customerId = pickString(entry.customerId, entry.CustomerId);
      const displayName = pickString(entry.displayName, entry.DisplayName);
      const primaryEmail = pickString(entry.primaryEmail, entry.PrimaryEmail);
      const status = pickString(entry.status, entry.Status);
      items.push({
        tenantId: tenantId ?? '',
        customerId: customerId ?? '',
        displayName: displayName ?? '',
        primaryEmail: primaryEmail ?? '',
        status: status ?? '',
      });
    }

    const size_ = pickNumber(raw.size, raw.Size) ?? size;
    const page_ = pickNumber(raw.page, raw.Page) ?? page;
    const totalCount = pickNumber(raw.totalCount, raw.TotalCount) ?? 0;
    const hasMore = page_ * size_ < totalCount;

    return { items, hasMore };
  }
}

function pick(camel: unknown, pascal: unknown): unknown {
  return camel !== undefined ? camel : pascal;
}

function pickString(camel: unknown, pascal: unknown): string | undefined {
  const value = pick(camel, pascal);
  return typeof value === 'string' ? value : undefined;
}

function pickNumber(camel: unknown, pascal: unknown): number | undefined {
  const value = pick(camel, pascal);
  return typeof value === 'number' ? value : undefined;
}
