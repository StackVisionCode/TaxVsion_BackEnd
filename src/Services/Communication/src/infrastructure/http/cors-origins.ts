import { config } from '../config.js';
import { platformSettings } from '../platform/platform-settings-source.js';

/**
 * Política CORS de HTTP y Socket.IO: los orígenes web de la plataforma (salen del dominio vigente en
 * PlatformSettings, así que siguen a un cambio de dominio sin redeploy) más los extra de config
 * (`COMMUNICATION_CORS_ORIGINS`: localhost y apps móviles). Sin ninguno de los dos (solo dev/test, ver
 * config.ts) acepta todo.
 */
export function isAllowedOrigin(origin: string | undefined): boolean {
  if (!origin) return true;
  const platformOrigins = platformSettings.webOrigins();
  if (config.cors.origins.length === 0 && platformOrigins.length === 0) return true;
  const normalized = origin.replace(/\/+$/, '').toLowerCase();
  return config.cors.origins.includes(origin) || platformOrigins.includes(normalized);
}
