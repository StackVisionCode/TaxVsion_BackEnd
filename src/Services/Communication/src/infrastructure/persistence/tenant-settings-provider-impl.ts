import type { PrismaClient } from '@prisma/client';
import type { Redis } from 'ioredis';
import type {
  TenantCommunicationSettingsSnapshot,
  TenantSettingsCacheInvalidator,
  TenantSettingsProvider,
} from '../../application/ports/tenant-settings-provider.js';

/**
 * Lee TenantCommunicationSettings con cache Redis 60s. Al modificar via
 * /communication/settings el use case llama a `invalidate` (T-13): la caché es Redis, compartida
 * por todas las instancias, así que un DEL basta sin pub/sub.
 *
 * Si la fila no existe (tenant nuevo), retorna defaults conservadores.
 */
export class RedisCachedTenantSettingsProvider implements TenantSettingsProvider, TenantSettingsCacheInvalidator {
  private static readonly CACHE_TTL_SECONDS = 60;

  constructor(
    private readonly prisma: PrismaClient,
    private readonly redis: Redis,
  ) {}

  async get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot> {
    const key = cacheKey(tenantId);
    const cached = await this.redis.get(key).catch(() => null);
    if (cached) {
      return JSON.parse(cached) as TenantCommunicationSettingsSnapshot;
    }

    const row = await this.prisma.tenantCommunicationSettings.findUnique({
      where: { TenantId: tenantId },
    });
    const snapshot: TenantCommunicationSettingsSnapshot = row
      ? {
          tenantId: row.TenantId,
          chatEnabled: row.ChatEnabled,
          employeeToEmployeeChatEnabled: row.EmployeeToEmployeeChatEnabled,
          restrictCustomerChatToAssignedPreparer: row.RestrictCustomerChatToAssignedPreparer,
          groupsAllowMultipleClients: row.GroupsAllowMultipleClients,
          screenshotsEnabled: row.ScreenshotsEnabled,
        }
      : {
          tenantId,
          chatEnabled: true,
          employeeToEmployeeChatEnabled: false,
          restrictCustomerChatToAssignedPreparer: false,
          groupsAllowMultipleClients: false,
          screenshotsEnabled: true,
        };
    await this.redis
      .set(key, JSON.stringify(snapshot), 'EX', RedisCachedTenantSettingsProvider.CACHE_TTL_SECONDS)
      .catch(() => undefined);
    return snapshot;
  }

  async invalidate(tenantId: string): Promise<void> {
    await this.redis.del(cacheKey(tenantId)).catch(() => undefined);
  }
}

const cacheKey = (tenantId: string): string => `comm:settings:${tenantId}`;
