/**
 * Resuelve el host primario (subdominio de plataforma) de un tenant, ej. "manfer.taxproffice.com".
 * Auth es la autoridad del dominio y el subdominio NO viaja en el request de invitación, así que se
 * consulta por M2M contra el endpoint interno de Auth (`internal/tenants/{id}/primary-host`). Nunca
 * lanza: cualquier fallo devuelve `null` y el caller cae al base fijo (frontendBaseUrl) — un link
 * degradado es mejor que fallar la creación de la invitación.
 */
export interface TenantHostResolver {
  resolveHost(tenantId: string): Promise<string | null>;
}
