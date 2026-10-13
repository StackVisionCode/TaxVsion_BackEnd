import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { PlatformSettingsSink, PlatformSnapshot } from '../../application/ports/platform-settings.js';

const REFRESH_MS = 5 * 60 * 1000;
const RETRY_MS = 30 * 1000;

/**
 * Marca y dominio de la plataforma tal como los publica Tenant (`GET platform/settings/public`). Espejo
 * Node del `PlatformSettingsProvider` de BuildingBlocks: arranca con el dominio de config (versión 0),
 * lee Tenant al iniciar y cada 5 minutos, y aplica el evento `platform.settings_changed.v1` en cuanto
 * llega. Una versión vieja nunca pisa a una nueva. De acá salen los orígenes web que acepta el CORS.
 */
export class PlatformSettingsSource implements PlatformSettingsSink {
  private current: PlatformSnapshot;
  private timer: NodeJS.Timeout | undefined;

  constructor(
    bootstrapBaseDomain: string,
    private readonly tenantBaseUrl: string | undefined,
  ) {
    this.current = { version: 0, baseDomain: normalize(bootstrapBaseDomain), brandName: '' };
  }

  get snapshot(): PlatformSnapshot {
    return this.current;
  }

  apply(next: PlatformSnapshot): boolean {
    if (next.version <= this.current.version || !next.baseDomain) return false;
    this.current = { ...next, baseDomain: normalize(next.baseDomain) };
    logger.info({ version: next.version, baseDomain: this.current.baseDomain }, 'platform settings applied');
    return true;
  }

  /** Orígenes de las apps de la plataforma: landing (apex y www), CRM (app) y portal (client). */
  webOrigins(): readonly string[] {
    const domain = this.current.baseDomain;
    if (!domain) return [];
    return [`https://${domain}`, `https://www.${domain}`, `https://app.${domain}`, `https://client.${domain}`];
  }

  start(): void {
    if (!this.tenantBaseUrl) {
      logger.info('platform settings: no tenant base url; using configured domain only');
      return;
    }
    const tick = async (): Promise<void> => {
      const answered = await this.refreshOnce();
      this.timer = setTimeout(() => void tick(), answered ? REFRESH_MS : RETRY_MS);
      this.timer.unref();
    };
    void tick();
  }

  stop(): void {
    if (this.timer) clearTimeout(this.timer);
  }

  /** Una lectura. Devuelve si Tenant contestó (aunque la versión ya fuera la vigente). */
  async refreshOnce(): Promise<boolean> {
    try {
      const response = await fetch(`${this.tenantBaseUrl!.replace(/\/+$/, '')}/platform/settings/public`);
      if (!response.ok) {
        logger.warn({ status: response.status }, 'platform settings: tenant returned non-2xx');
        return false;
      }
      const body = (await response.json()) as Partial<PlatformSnapshot>;
      if (typeof body.version === 'number' && typeof body.baseDomain === 'string') {
        this.apply({ version: body.version, baseDomain: body.baseDomain, brandName: body.brandName ?? '' });
      }
      return true;
    } catch (err) {
      logger.warn({ err: (err as Error).message }, 'platform settings: tenant unreachable');
      return false;
    }
  }
}

function normalize(domain: string): string {
  return domain.trim().replace(/\.+$/, '').toLowerCase();
}

export const platformSettings = new PlatformSettingsSource(config.platform.baseDomain, config.platform.tenantBaseUrl);
