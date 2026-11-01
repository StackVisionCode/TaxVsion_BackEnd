/**
 * Snapshot de settings de oficina que leen los use cases de chat/llamadas/meetings (flags on/off).
 * Se resuelven por tenant y se cachean 60 s; la escritura en /communication/settings invalida la
 * entrada al instante (T-13). Los valores numéricos y techos NO van aquí: salen del
 * EffectiveSettingsResolver. `internalGroupsEnabled` dejó de ser de oficina (D-16).
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
  /** F8b (D-20): varios clientes en un mismo grupo; ausente = false (uno solo). */
  readonly groupsAllowMultipleClients?: boolean;
  readonly screenshotsEnabled: boolean;
}

export interface TenantSettingsProvider {
  get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot>;
}

/** Quien escribe settings de oficina borra la entrada cacheada para que aplique al instante. */
export interface TenantSettingsCacheInvalidator {
  invalidate(tenantId: string): Promise<void>;
}
