/**
 * Snapshot de settings que afectan a Fase 1. Se resuelven por tenant y se
 * cachean 60s con invalidacion via Redis Pub/Sub cuando el tenant admin
 * modifica /communication/settings (Fase 6).
 */
export interface TenantCommunicationSettingsSnapshot {
  readonly tenantId: string;
  readonly chatEnabled: boolean;
  readonly employeeToEmployeeChatEnabled: boolean;
  readonly screenshotsEnabled: boolean;
  readonly internalGroupsEnabled: boolean;
  readonly messageRetentionDays: number;
}

export interface TenantSettingsProvider {
  get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot>;
}
