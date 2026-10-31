import type { PrismaClient } from '@prisma/client';
import type {
  ConfigurationAuditEntry,
  ConfigurationAuditReason,
  ConfigurationAuditScope,
} from '../../domain/settings/configuration-audit.js';
import type {
  ConfigurationAuditFilter,
  ConfigurationAuditRepository,
} from '../../application/ports/configuration-audit-repository.js';

export class PrismaConfigurationAuditRepository implements ConfigurationAuditRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async append(entries: readonly ConfigurationAuditEntry[]): Promise<void> {
    if (entries.length === 0) return;
    await this.prisma.configurationAudit.createMany({
      data: entries.map((e) => ({
        Id: e.id,
        TenantId: e.tenantId,
        Scope: e.scope,
        Key: e.key,
        OldValue: e.oldValue,
        NewValue: e.newValue,
        ActorUserId: e.actorUserId,
        Reason: e.reason,
        CorrelationId: e.correlationId,
        CreatedAtUtc: e.createdAtUtc,
      })),
    });
  }

  async list(filter: ConfigurationAuditFilter): Promise<readonly ConfigurationAuditEntry[]> {
    const rows = await this.prisma.configurationAudit.findMany({
      where: {
        ...(filter.tenantId !== undefined ? { TenantId: filter.tenantId } : {}),
        ...(filter.scope !== undefined ? { Scope: filter.scope } : {}),
      },
      orderBy: { CreatedAtUtc: 'desc' },
      skip: filter.skip,
      take: filter.take,
    });
    return rows.map((r) => ({
      id: r.Id,
      tenantId: r.TenantId,
      scope: r.Scope as ConfigurationAuditScope,
      key: r.Key,
      oldValue: r.OldValue,
      newValue: r.NewValue,
      actorUserId: r.ActorUserId,
      reason: r.Reason as ConfigurationAuditReason,
      correlationId: r.CorrelationId,
      createdAtUtc: r.CreatedAtUtc,
    }));
  }
}
