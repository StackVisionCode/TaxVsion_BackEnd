/** Capas de configuración que se auditan (una fila por clave cambiada). */
export const ConfigurationAuditScope = {
  Platform: 'Platform',
  TenantConstraints: 'TenantConstraints',
  TenantSettings: 'TenantSettings',
  UserPreferences: 'UserPreferences',
} as const;
export type ConfigurationAuditScope = (typeof ConfigurationAuditScope)[keyof typeof ConfigurationAuditScope];

/** "Update" = lo pidió un actor; "AutoCorrect" = PlatformAdmin bajó un techo y la oficina se ajustó sola. */
export const ConfigurationAuditReason = {
  Update: 'Update',
  AutoCorrect: 'AutoCorrect',
} as const;
export type ConfigurationAuditReason = (typeof ConfigurationAuditReason)[keyof typeof ConfigurationAuditReason];

export interface ConfigurationAuditEntry {
  readonly id: string;
  /** null = cambio global de plataforma. */
  readonly tenantId: string | null;
  readonly scope: ConfigurationAuditScope;
  readonly key: string;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly actorUserId: string | null;
  readonly reason: ConfigurationAuditReason;
  readonly correlationId: string | null;
  readonly createdAtUtc: Date;
}
