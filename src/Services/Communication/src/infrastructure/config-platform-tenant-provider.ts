import type { PlatformTenantProvider } from '../application/ports/platform-tenant-provider.js';
import { config } from './config.js';

export class ConfigPlatformTenantProvider implements PlatformTenantProvider {
  getPlatformTenantId(): string {
    return config.platformTenantId;
  }
}
