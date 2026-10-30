import type { ConfigurationAuditEntry } from '../../domain/settings/configuration-audit.js';
import type { ConfigurationAuditFilter, ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';

export interface ConfigurationAuditEntryDto {
  readonly id: string;
  readonly tenantId: string | null;
  readonly scope: string;
  readonly key: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly actorUserId: string | null;
  readonly reason: string;
  readonly correlationId: string | null;
  readonly createdAtUtc: string;
}

export async function listConfigurationAudit(
  filter: ConfigurationAuditFilter,
  deps: { configurationAudit: ConfigurationAuditRepository },
): Promise<{ items: ConfigurationAuditEntryDto[]; skip: number; take: number }> {
  const entries = await deps.configurationAudit.list(filter);
  return { items: entries.map(toDto), skip: filter.skip, take: filter.take };
}

function toDto(entry: ConfigurationAuditEntry): ConfigurationAuditEntryDto {
  return {
    id: entry.id,
    tenantId: entry.tenantId,
    scope: entry.scope,
    key: entry.key,
    oldValue: entry.oldValue,
    newValue: entry.newValue,
    actorUserId: entry.actorUserId,
    reason: entry.reason,
    correlationId: entry.correlationId,
    createdAtUtc: entry.createdAtUtc.toISOString(),
  };
}
