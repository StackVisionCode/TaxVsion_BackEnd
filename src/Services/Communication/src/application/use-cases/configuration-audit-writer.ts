import { randomUUID } from 'node:crypto';
import type { ConfigurationChange } from '../../domain/settings/configuration-change.js';
import {
  type ConfigurationAuditEntry,
  type ConfigurationAuditReason,
  type ConfigurationAuditScope,
} from '../../domain/settings/configuration-audit.js';

/** Convierte los cambios de un aggregate en filas de auditoría (una por clave). */
export function buildAuditEntries(input: {
  tenantId: string | null;
  scope: ConfigurationAuditScope;
  reason: ConfigurationAuditReason;
  actorUserId: string | null;
  correlationId: string | null;
  changes: readonly ConfigurationChange[];
  now: Date;
}): ConfigurationAuditEntry[] {
  const entries: ConfigurationAuditEntry[] = [];
  for (const change of input.changes) {
    entries.push({
      id: randomUUID(),
      tenantId: input.tenantId,
      scope: input.scope,
      key: change.key,
      oldValue: change.oldValue,
      newValue: change.newValue,
      actorUserId: input.actorUserId,
      reason: input.reason,
      correlationId: input.correlationId,
      createdAtUtc: input.now,
    });
  }
  return entries;
}
