import type { TenantHostResolver } from '../ports/tenant-host-resolver.js';
import type { TenantTimeZoneStore } from '../ports/tenant-time-zone-store.js';

/**
 * Host por Auth, zona por la proyección local de Tenant. Así la zona ya no depende de que la oficina tenga
 * subdominio ni espera la caché de 15 min. Sin fila (o si la base falla) cae al pull de Auth. Nunca lanza.
 */
export class ProjectedTimeZoneHostResolver implements TenantHostResolver {
  constructor(
    private readonly zones: Pick<TenantTimeZoneStore, 'find'>,
    private readonly inner: TenantHostResolver,
  ) {}

  resolveHost(tenantId: string): Promise<string | null> {
    return this.inner.resolveHost(tenantId);
  }

  async resolveTimeZone(tenantId: string): Promise<string | null> {
    try {
      const projected = await this.zones.find(tenantId);
      if (projected) return projected.timeZoneId;
    } catch {
      // Respaldo: el pull de Auth.
    }
    return (await this.inner.resolveTimeZone?.(tenantId)) ?? null;
  }
}
