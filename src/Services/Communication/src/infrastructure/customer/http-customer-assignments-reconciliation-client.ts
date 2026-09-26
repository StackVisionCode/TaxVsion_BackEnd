import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * Lector cross-tenant del set M:N de asignaciones customer↔staff para el
 * reconciliador periodico (P2.5). Llama al endpoint M2M
 * `GET internal/customers/assignments/reconciliation?page={n}&size={n}` de
 * Customer.Api (ServiceOnly + solo PlatformTenant), que devuelve solo los
 * customers CON asignaciones. Adquiere el token con `config.platformTenantId`
 * como sentinel, igual que HttpCustomerReconciliationClient (directorio).
 *
 * Fail-soft total: cualquier fallo (token, red, status, parseo) devuelve null y
 * el scheduler corta la pasada sin lanzar — nunca revienta un tick.
 */
export interface CustomerAssignmentsReconciliationRow {
  readonly tenantId: string;
  readonly customerId: string;
  readonly assigneeUserIds: string[];
  readonly version: Date | null;
}

export interface CustomerAssignmentsReconciliationPage {
  readonly items: CustomerAssignmentsReconciliationRow[];
  readonly hasMore: boolean;
}

interface RawRow {
  readonly tenantId?: unknown;
  readonly TenantId?: unknown;
  readonly customerId?: unknown;
  readonly CustomerId?: unknown;
  readonly assigneeUserIds?: unknown;
  readonly AssigneeUserIds?: unknown;
  readonly version?: unknown;
  readonly Version?: unknown;
}

interface RawResponse {
  readonly items?: unknown;
  readonly Items?: unknown;
  readonly page?: unknown;
  readonly Page?: unknown;
  readonly size?: unknown;
  readonly Size?: unknown;
  readonly totalCount?: unknown;
  readonly TotalCount?: unknown;
}

export class HttpCustomerAssignmentsReconciliationClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async listPage(page: number, size: number): Promise<CustomerAssignmentsReconciliationPage | null> {
    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'could not acquire service token for assignment reconciliation; skipping');
      return null;
    }

    let response: Response;
    try {
      const url = `${config.customer.baseUrl}/internal/customers/assignments/reconciliation?page=${page}&size=${size}`;
      response = await fetch(url, { headers: { authorization: `Bearer ${token}` } });
    } catch (error) {
      logger.warn({ error, page }, 'assignment reconciliation request failed; skipping');
      return null;
    }

    if (!response.ok) {
      logger.warn({ status: response.status, page }, 'assignment reconciliation request failed; skipping');
      return null;
    }

    let raw: RawResponse;
    try {
      raw = (await response.json()) as RawResponse;
    } catch (error) {
      logger.warn({ error, page }, 'assignment reconciliation response was not valid JSON; skipping');
      return null;
    }

    const rawItems = pick(raw.items, raw.Items);
    if (!Array.isArray(rawItems)) return null;

    const items: CustomerAssignmentsReconciliationRow[] = [];
    for (const entry of rawItems as RawRow[]) {
      const tenantId = pickString(entry.tenantId, entry.TenantId);
      const customerId = pickString(entry.customerId, entry.CustomerId);
      if (!tenantId || !customerId) continue;
      const versionStr = pickString(entry.version, entry.Version);
      const parsedVersion = versionStr ? new Date(versionStr) : null;
      items.push({
        tenantId,
        customerId,
        assigneeUserIds: pickStringArray(entry.assigneeUserIds, entry.AssigneeUserIds),
        version: parsedVersion && !Number.isNaN(parsedVersion.getTime()) ? parsedVersion : null,
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

function pickStringArray(camel: unknown, pascal: unknown): string[] {
  const value = pick(camel, pascal);
  if (!Array.isArray(value)) return [];
  return value.filter((item): item is string => typeof item === 'string');
}
