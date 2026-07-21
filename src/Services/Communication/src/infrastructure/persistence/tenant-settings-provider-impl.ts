import type { PrismaClient } from '@prisma/client';
import type { Redis } from 'ioredis';
import type {
  TenantCommunicationSettingsSnapshot,
  TenantSettingsProvider,
} from '../../application/ports/tenant-settings-provider.js';

/**
 * Lee TenantCommunicationSettings con cache Redis 60s. Al modificar via
 * /communication/settings (Fase 6) se publica un pub/sub `comm:settings:invalidate:{tenantId}`
 * y el subscriber invalida la key.
 *
 * Si la fila no existe (tenant nuevo), retorna defaults conservadores.
 */
export class RedisCachedTenantSettingsProvider implements TenantSettingsProvider {
  private static readonly CACHE_TTL_SECONDS = 60;

  constructor(
    private readonly prisma: PrismaClient,
    private readonly redis: Redis,
  ) {}

  async get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot> {
    const key = `comm:settings:${tenantId}`;
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
          screenshotsEnabled: row.ScreenshotsEnabled,
          internalGroupsEnabled: row.InternalGroupsEnabled,
          messageRetentionDays: row.MessageRetentionDays,
        }
      : {
          tenantId,
          chatEnabled: true,
          employeeToEmployeeChatEnabled: false,
          restrictCustomerChatToAssignedPreparer: false,
          screenshotsEnabled: true,
          internalGroupsEnabled: false,
          messageRetentionDays: 365,
        };
    await this.redis
      .set(key, JSON.stringify(snapshot), 'EX', RedisCachedTenantSettingsProvider.CACHE_TTL_SECONDS)
      .catch(() => undefined);
    return snapshot;
  }
}
