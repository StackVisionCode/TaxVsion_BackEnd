/**
 * El GUID fijo del PlatformTenant se sembra en Tenant/Auth como
 * `8f58a521-4c25-4d91-9f4e-7ad5df14c001` (README §14). Comunicacion lo lee de
 * config (nunca hardcodeado en dominio) para poder cambiarlo en test o entornos
 * de compliance especificos.
 */
export interface PlatformTenantProvider {
  getPlatformTenantId(): string;
}
