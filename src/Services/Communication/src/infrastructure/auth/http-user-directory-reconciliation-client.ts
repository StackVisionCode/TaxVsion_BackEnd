import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from './service-token-client.js';

/**
 * Lector cross-tenant del directorio de usuarios de Auth para el reconciliador de
 * UserDirectoryEntry (F5 de avatares). Llama al endpoint M2M
 * `GET internal/users/directory/reconciliation?page={n}&size={n}` (ServiceOnly, solo acepta un
 * service token del PlatformTenant). Mismo patron que HttpCustomerReconciliationClient: una sola
 * credencial M2M con `config.platformTenantId` como sentinel cubre todos los tenants.
 *
 * Fail-soft total: cualquier fallo (token, red, status, parseo) devuelve null y el scheduler corta la
 * pasada sin lanzar.
 */
export interface UserDirectoryReconciliationRow {
  readonly tenantId: string;
  readonly userId: string;
  readonly actorType: string;
  readonly isActive: boolean;
  readonly displayName: string;
  readonly email: string;
  readonly avatarFileId: string | null;
}

export interface UserDirectoryReconciliationPage {
  readonly items: UserDirectoryReconciliationRow[];
  readonly hasMore: boolean;
}

type RawRow = Readonly<Record<string, unknown>>;

export class HttpUserDirectoryReconciliationClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async listPage(page: number, size: number): Promise<UserDirectoryReconciliationPage | null> {
    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'could not acquire service token for user directory reconciliation; skipping');
      return null;
    }

    let response: Response;
    try {
      const url = `${config.serviceAuth.authBaseUrl}/internal/users/directory/reconciliation?page=${page}&size=${size}`;
      response = await fetch(url, { headers: { authorization: `Bearer ${token}` } });
    } catch (error) {
      logger.warn({ error, page }, 'user directory reconciliation request failed; skipping');
      return null;
    }

    if (!response.ok) {
      logger.warn(
        { status: response.status, page },
        'user directory reconciliation request failed; skipping',
      );
      return null;
    }

    let raw: RawRow;
    try {
      raw = (await response.json()) as RawRow;
    } catch (error) {
      logger.warn({ error, page }, 'user directory reconciliation response was not valid JSON; skipping');
      return null;
    }

    const rawItems = pick(raw, 'items', 'Items');
    if (!Array.isArray(rawItems)) return null;

    const items: UserDirectoryReconciliationRow[] = (rawItems as RawRow[]).map((entry) => {
      const avatar = pick(entry, 'avatarFileId', 'AvatarFileId');
      const isActive = pick(entry, 'isActive', 'IsActive');
      return {
        tenantId: pickString(entry, 'tenantId', 'TenantId') ?? '',
        userId: pickString(entry, 'userId', 'UserId') ?? '',
        actorType: pickString(entry, 'actorType', 'ActorType') ?? '',
        isActive: isActive === true,
        displayName: pickString(entry, 'displayName', 'DisplayName') ?? '',
        email: pickString(entry, 'email', 'Email') ?? '',
        avatarFileId: typeof avatar === 'string' && avatar.length > 0 ? avatar.toLowerCase() : null,
      };
    });

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
