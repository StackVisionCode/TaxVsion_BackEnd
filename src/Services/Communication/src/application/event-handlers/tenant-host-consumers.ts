import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { TenantHostCache } from '../ports/tenant-host-cache.js';
import type { PlatformSettingsSink } from '../ports/platform-settings.js';

/** Alias de los eventos que invalidan el host cacheado (ver CLR_TYPE_TO_EVENT_TYPE en consumer-runtime). */
export const TenantHostEventTypes = {
  SubdomainChanged: 'auth.tenant.subdomain_changed.v1',
  PlatformSettingsChanged: 'platform.settings_changed.v1',
} as const;

/**
 * Mantiene al día la cache de host primario por oficina: al renombrar el subdominio se olvida esa
 * oficina; al cambiar el dominio de la plataforma se olvidan todas (el próximo link se vuelve a pedir a
 * Auth, que ya responde con el host nuevo) y se aplica la versión nueva de PlatformSettings, de la que
 * salen los orígenes que acepta el CORS.
 */
export function bindTenantHostConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { hostCache: TenantHostCache; platformSettings?: PlatformSettingsSink },
): void {
  register(TenantHostEventTypes.SubdomainChanged, async (env) => {
    if (env.tenantId) deps.hostCache.forget(env.tenantId);
  });

  register(TenantHostEventTypes.PlatformSettingsChanged, async (env) => {
    deps.hostCache.forgetAll();
    const version = env.payload['version'] ?? env.payload['Version'];
    const baseDomain = env.payload['baseDomain'] ?? env.payload['BaseDomain'];
    const brandName = env.payload['brandName'] ?? env.payload['BrandName'];
    if (typeof version === 'number' && typeof baseDomain === 'string') {
      deps.platformSettings?.apply({ version, baseDomain, brandName: typeof brandName === 'string' ? brandName : '' });
    }
  });
}
