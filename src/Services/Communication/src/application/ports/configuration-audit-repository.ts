import type { ConfigurationAuditEntry, ConfigurationAuditScope } from '../../domain/settings/configuration-audit.js';

export interface ConfigurationAuditFilter {
  readonly tenantId?: string;
  readonly scope?: ConfigurationAuditScope;
  readonly skip: number;
  readonly take: number;
}

export interface ConfigurationAuditRepository {
  append(entries: readonly ConfigurationAuditEntry[]): Promise<void>;
  list(filter: ConfigurationAuditFilter): Promise<readonly ConfigurationAuditEntry[]>;
}
