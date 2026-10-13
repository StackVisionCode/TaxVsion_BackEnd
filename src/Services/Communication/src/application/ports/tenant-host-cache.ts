/**
 * Invalida el host primario cacheado de las oficinas. Lo usan los consumers cuando el host deja de
 * ser válido: una oficina renombró su subdominio (se olvida esa) o la plataforma cambió de dominio
 * (se olvidan todas). Sin esto, el link de invitación a una reunión salía hacia el host viejo durante
 * los 15 minutos de la cache.
 */
export interface TenantHostCache {
  forget(tenantId: string): void;
  forgetAll(): void;
}
