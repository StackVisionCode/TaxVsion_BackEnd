/**
 * Resuelve el host primario (subdominio de plataforma) de un tenant, ej. "manfer.taxoffix.io".
 * Auth es la autoridad del dominio y el subdominio NO viaja en el request de invitación, así que se
 * consulta por M2M contra el endpoint interno de Auth (`internal/tenants/{id}/primary-host`). Nunca
 * lanza: cualquier fallo devuelve `null` y el caller cae al base fijo (frontendBaseUrl) — un link
 * degradado es mejor que fallar la creación de la invitación.
 */
export interface TenantHostResolver {
  resolveHost(tenantId: string): Promise<string | null>;
  /** F6 (T-64): zona horaria IANA de la oficina (misma consulta M2M, cacheada); null si Auth no la da. */
  resolveTimeZone?(tenantId: string): Promise<string | null>;
}
