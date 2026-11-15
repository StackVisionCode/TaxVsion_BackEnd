/**
 * Copia local de la zona IANA de una oficina (dueño: Tenant). Mismas reglas que la proyección .NET
 * `BuildingBlocks.TenantTimeZones.TenantTimeZoneProjection`: con las dos versiones manda la versión; si falta
 * alguna, manda la hora del cambio.
 */
export interface TenantTimeZone {
  readonly tenantId: string;
  readonly timeZoneId: string;
  /** Versión de la oficina en Tenant; null en el alta o en eventos viejos. */
  readonly sourceVersion: number | null;
  /** Momento del cambio en Tenant (o de la última lectura de la reconciliación). */
  readonly sourceUpdatedAtUtc: Date;
}

export interface TenantTimeZoneChange {
  readonly tenantId: string;
  readonly timeZoneId: string;
  readonly sourceVersion: number | null;
  readonly changedAtUtc: Date;
}

/** El id recortado si el motor lo reconoce como zona IANA; null si no. */
export function normalizeTimeZoneId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const id = value.trim();
  if (!id) return null;
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: id });
    return id;
  } catch {
    return null;
  }
}

function isOlderThan(current: TenantTimeZone, version: number | null, changedAtUtc: Date): boolean {
  return version !== null && current.sourceVersion !== null
    ? version > current.sourceVersion
    : changedAtUtc.getTime() > current.sourceUpdatedAtUtc.getTime();
}

/** El estado nuevo de la fila, o null si el cambio es igual o más viejo que lo ya aplicado. */
export function projectChange(current: TenantTimeZone | null, change: TenantTimeZoneChange): TenantTimeZone | null {
  if (current === null) {
    return {
      tenantId: change.tenantId,
      timeZoneId: change.timeZoneId,
      sourceVersion: change.sourceVersion,
      sourceUpdatedAtUtc: change.changedAtUtc,
    };
  }
  if (!isOlderThan(current, change.sourceVersion, change.changedAtUtc)) return null;
  return {
    tenantId: current.tenantId,
    timeZoneId: change.timeZoneId,
    sourceVersion: change.sourceVersion ?? current.sourceVersion,
    sourceUpdatedAtUtc: change.changedAtUtc,
  };
}

/**
 * Corrige con el estado actual leído de Tenant (sin versión). Deja la hora de lectura: un evento más viejo que
 * llegue después no lo deshace; uno más nuevo, sí. Null si la zona ya coincide.
 */
export function reconcileWithSource(
  current: TenantTimeZone | null,
  tenantId: string,
  timeZoneId: string,
  readAtUtc: Date,
): TenantTimeZone | null {
  if (current === null) return { tenantId, timeZoneId, sourceVersion: null, sourceUpdatedAtUtc: readAtUtc };
  if (current.timeZoneId === timeZoneId) return null;
  return {
    tenantId,
    timeZoneId,
    sourceVersion: current.sourceVersion,
    sourceUpdatedAtUtc: readAtUtc > current.sourceUpdatedAtUtc ? readAtUtc : current.sourceUpdatedAtUtc,
  };
}
