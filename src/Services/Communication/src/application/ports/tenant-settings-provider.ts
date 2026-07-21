/**
 * Snapshot de settings que afectan a Fase 1. Se resuelven por tenant y se
 * cachean 60s con invalidacion via Redis Pub/Sub cuando el tenant admin
 * modifica /communication/settings (Fase 6).
 */
export interface TenantCommunicationSettingsSnapshot {
  readonly tenantId: string;
  readonly chatEnabled: boolean;
  readonly employeeToEmployeeChatEnabled: boolean;
  /**
   * Fase B5 (chat tipado) — si esta en true, un chat directo cliente↔empleado
   * solo se permite si el empleado es justo el preparador asignado del
   * customer (CustomerPreparerAssignment). Default false, opt-in por tenant.
   */
  readonly restrictCustomerChatToAssignedPreparer: boolean;
  readonly screenshotsEnabled: boolean;
  readonly internalGroupsEnabled: boolean;
  readonly messageRetentionDays: number;
}

export interface TenantSettingsProvider {
  get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot>;
}
