import type { TenantCommunicationSettings } from '../../domain/settings/tenant-communication-settings.js';
import type { TenantCommunicationLimitsSnapshot } from '../../domain/settings/tenant-communication-limits.js';

export interface SettingsRepository {
  findByTenantId(tenantId: string): Promise<TenantCommunicationSettings | null>;
  save(settings: TenantCommunicationSettings): Promise<void>;
}

export interface LimitsRepository {
  findByTenantId(tenantId: string): Promise<TenantCommunicationLimitsSnapshot | null>;
  upsert(snapshot: TenantCommunicationLimitsSnapshot): Promise<void>;
  markSuspended(tenantId: string, suspended: boolean, now: Date): Promise<void>;
}
