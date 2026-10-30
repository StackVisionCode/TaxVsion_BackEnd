import { randomUUID } from 'node:crypto';
import type { EffectiveSettingsResolver } from '../../src/application/ports/effective-settings-resolver.js';
import {
  type EffectiveCommunicationSettings,
  resolveEffectiveSettings,
} from '../../src/domain/settings/effective-communication-settings.js';
import {
  PlatformCommunicationSettings,
  type PlatformCommunicationSettingsSnapshot,
} from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';

/** Valores efectivos de una oficina con todo en defaults técnicos (sin plan ni techos). */
export function effectiveDefaults(
  tenantId: string = randomUUID(),
  platformPatch: Partial<PlatformCommunicationSettingsSnapshot> = {},
): EffectiveCommunicationSettings {
  const platform = { ...PlatformCommunicationSettings.defaults().toSnapshot(), ...platformPatch };
  return resolveEffectiveSettings({
    platform,
    limits: null,
    constraints: null,
    tenant: TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
  });
}

/** Resolver en memoria: devuelve siempre los mismos valores y cuenta las invalidaciones. */
export class FakeEffectiveSettingsResolver implements EffectiveSettingsResolver {
  readonly invalidatedTenants: string[] = [];
  platformInvalidations = 0;

  constructor(private readonly platformPatch: Partial<PlatformCommunicationSettingsSnapshot> = {}) {}

  async resolve(tenantId: string): Promise<EffectiveCommunicationSettings> {
    return effectiveDefaults(tenantId, this.platformPatch);
  }

  async resolveForUser(tenantId: string): Promise<EffectiveCommunicationSettings> {
    return effectiveDefaults(tenantId, this.platformPatch);
  }

  async platform(): Promise<PlatformCommunicationSettingsSnapshot> {
    return { ...PlatformCommunicationSettings.defaults().toSnapshot(), ...this.platformPatch };
  }

  async invalidateTenant(tenantId: string): Promise<void> {
    this.invalidatedTenants.push(tenantId);
  }

  async invalidatePlatform(): Promise<void> {
    this.platformInvalidations += 1;
  }
}
