import type { TenantTimeZone } from '../../domain/time-zones/tenant-time-zone.js';

/** Proyección local de la zona de cada oficina (tabla `TenantTimeZoneProjection`). */
export interface TenantTimeZoneStore {
  find(tenantId: string): Promise<TenantTimeZone | null>;
  /** Inserta o reemplaza la fila de la oficina. */
  save(zone: TenantTimeZone): Promise<void>;
}
