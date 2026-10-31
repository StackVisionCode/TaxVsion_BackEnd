import type { TenantCommunicationConstraints } from '../../domain/settings/tenant-communication-constraints.js';

export interface TenantCommunicationConstraintsRepository {
  findByTenantId(tenantId: string): Promise<TenantCommunicationConstraints | null>;
  save(constraints: TenantCommunicationConstraints): Promise<void>;
}
