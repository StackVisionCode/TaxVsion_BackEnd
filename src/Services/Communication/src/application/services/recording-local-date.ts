import type { TenantTimeZoneStore } from '../ports/tenant-time-zone-store.js';
import { localDate } from './office-clock.js';

/**
 * P25: fecha local (`yyyy-MM-dd`) de una grabación en la zona de la oficina, para nombrar los archivos nuevos. Sin
 * proyección configurada devuelve undefined y el worker usa su fecha de siempre; sin fila, la UTC.
 */
export async function recordingLocalDate(
  zones: Pick<TenantTimeZoneStore, 'find'> | undefined,
  tenantId: string,
  at: Date,
): Promise<string | undefined> {
  if (!zones) return undefined;
  const zone = await zones.find(tenantId).catch(() => null);
  return localDate(at, zone?.timeZoneId ?? 'UTC');
}
