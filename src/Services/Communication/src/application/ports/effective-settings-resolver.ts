import type { EffectiveCommunicationSettings } from '../../domain/settings/effective-communication-settings.js';
import type { PlatformCommunicationSettingsSnapshot } from '../../domain/settings/platform-communication-settings.js';

/**
 * Único punto de lectura de la configuración vigente (T-42/T-43): global → plan → techo de
 * oficina → oficina → usuario, con caché por oficina. Fail-safe: si una capa no se puede leer,
 * resuelve con los defaults técnicos en vez de fallar la operación que lo consulta.
 */
export interface EffectiveSettingsResolver {
  resolve(tenantId: string): Promise<EffectiveCommunicationSettings>;
  resolveForUser(tenantId: string, userId: string): Promise<EffectiveCommunicationSettings>;
  platform(): Promise<PlatformCommunicationSettingsSnapshot>;
  invalidateTenant(tenantId: string): Promise<void>;
  invalidatePlatform(): Promise<void>;
}
