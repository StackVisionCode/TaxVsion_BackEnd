import type { Redis } from 'ioredis';
import {
  type EffectiveCommunicationSettings,
  resolveEffectiveSettings,
  resolveUserSection,
} from '../../domain/settings/effective-communication-settings.js';
import {
  PlatformCommunicationSettings,
  type PlatformCommunicationSettingsSnapshot,
} from '../../domain/settings/platform-communication-settings.js';
import { TenantCommunicationSettings } from '../../domain/settings/tenant-communication-settings.js';
import type { EffectiveSettingsResolver } from '../../application/ports/effective-settings-resolver.js';
import type { PlatformCommunicationSettingsRepository } from '../../application/ports/platform-communication-settings-repository.js';
import type { TenantCommunicationConstraintsRepository } from '../../application/ports/tenant-communication-constraints-repository.js';
import type { UserCommunicationPreferencesRepository } from '../../application/ports/user-communication-preferences-repository.js';
import type { LimitsRepository, SettingsRepository } from '../../application/ports/settings-repository.js';
import { logger } from '../logger/logger.js';

export interface EffectiveSettingsResolverDeps {
  readonly platform: PlatformCommunicationSettingsRepository;
  readonly limits: LimitsRepository;
  readonly constraints: TenantCommunicationConstraintsRepository;
  readonly tenantSettings: SettingsRepository;
  readonly preferences: UserCommunicationPreferencesRepository;
}

const PLATFORM_KEY = 'comm:platform-settings';
const tenantKey = (tenantId: string): string => `comm:effective:${tenantId}`;
/** Redis es la caché compartida entre instancias; el TTL solo acota una invalidación perdida. */
const REDIS_TTL_SECONDS = 60;
/** Capa en proceso para rutas calientes (dominant_speaker, disconnect): corta en segundos. */
const LOCAL_TTL_MS = 5_000;

interface LocalEntry<T> {
  readonly value: T;
  readonly expiresAtMs: number;
}

/**
 * Resolver con dos niveles de caché. Un cambio global sube `platform.version`: la entrada por
 * oficina guarda la versión con la que se calculó y se recomputa si ya no coincide, así una
 * escritura global no tiene que barrer las claves de todas las oficinas.
 */
export class RedisEffectiveSettingsResolver implements EffectiveSettingsResolver {
  private readonly localTenants = new Map<string, LocalEntry<EffectiveCommunicationSettings>>();
  private localPlatform: LocalEntry<PlatformCommunicationSettingsSnapshot> | null = null;

  constructor(
    private readonly redis: Redis,
    private readonly deps: EffectiveSettingsResolverDeps,
  ) {}

  async platform(): Promise<PlatformCommunicationSettingsSnapshot> {
    const now = Date.now();
    if (this.localPlatform && this.localPlatform.expiresAtMs > now) return this.localPlatform.value;

    const cached = await this.redis.get(PLATFORM_KEY).catch(() => null);
    if (cached) {
      const snapshot = reviveDates(JSON.parse(cached)) as unknown as PlatformCommunicationSettingsSnapshot;
      this.localPlatform = { value: snapshot, expiresAtMs: now + LOCAL_TTL_MS };
      return snapshot;
    }

    let snapshot: PlatformCommunicationSettingsSnapshot;
    try {
      snapshot = (await this.deps.platform.get()).toSnapshot();
    } catch (err) {
      // Fail-safe: sin BD se resuelve con los defaults técnicos, nunca se bloquea al llamador.
      logger.error({ err }, 'EffectiveSettingsResolver: platform settings unavailable, using technical defaults');
      return PlatformCommunicationSettings.defaults().toSnapshot();
    }
    await this.redis.set(PLATFORM_KEY, JSON.stringify(snapshot), 'EX', REDIS_TTL_SECONDS).catch(() => undefined);
    this.localPlatform = { value: snapshot, expiresAtMs: now + LOCAL_TTL_MS };
    return snapshot;
  }

  async resolve(tenantId: string): Promise<EffectiveCommunicationSettings> {
    const platform = await this.platform();
    const now = Date.now();
    const local = this.localTenants.get(tenantId);
    if (local && local.expiresAtMs > now && local.value.platformVersion === platform.version) return local.value;

    const cached = await this.redis.get(tenantKey(tenantId)).catch(() => null);
    if (cached) {
      const effective = JSON.parse(cached) as EffectiveCommunicationSettings;
      if (effective.platformVersion === platform.version) {
        this.localTenants.set(tenantId, { value: effective, expiresAtMs: now + LOCAL_TTL_MS });
        return effective;
      }
    }

    const effective = await this.compute(tenantId, platform);
    await this.redis
      .set(tenantKey(tenantId), JSON.stringify(effective), 'EX', REDIS_TTL_SECONDS)
      .catch(() => undefined);
    this.localTenants.set(tenantId, { value: effective, expiresAtMs: now + LOCAL_TTL_MS });
    return effective;
  }

  /** Las preferencias no se cachean: se piden en pocos sitios y deben aplicar al instante. */
  async resolveForUser(tenantId: string, userId: string): Promise<EffectiveCommunicationSettings> {
    const base = await this.resolve(tenantId);
    const preferences = await this.deps.preferences.find(tenantId, userId).catch(() => null);
    return { ...base, user: resolveUserSection(preferences?.toSnapshot() ?? null, base.recording.transcriptLanguage) };
  }

  async invalidateTenant(tenantId: string): Promise<void> {
    this.localTenants.delete(tenantId);
    await this.redis.del(tenantKey(tenantId)).catch(() => undefined);
  }

  async invalidatePlatform(): Promise<void> {
    this.localPlatform = null;
    this.localTenants.clear();
    await this.redis.del(PLATFORM_KEY).catch(() => undefined);
  }

  private async compute(
    tenantId: string,
    platform: PlatformCommunicationSettingsSnapshot,
  ): Promise<EffectiveCommunicationSettings> {
    const [limits, constraints, tenant] = await Promise.all([
      this.deps.limits.findByTenantId(tenantId).catch(() => null),
      this.deps.constraints.findByTenantId(tenantId).catch(() => null),
      this.deps.tenantSettings.findByTenantId(tenantId).catch(() => null),
    ]);
    return resolveEffectiveSettings({
      platform,
      limits,
      constraints: constraints ? constraints.toSnapshot() : null,
      tenant: (tenant ?? TenantCommunicationSettings.defaults(tenantId)).toSnapshot(),
    });
  }
}

/** JSON pierde las fechas; solo `updatedAtUtc` las necesita de vuelta. */
function reviveDates(raw: Record<string, unknown>): Record<string, unknown> {
  const updatedAtUtc = raw['updatedAtUtc'];
  return typeof updatedAtUtc === 'string' ? { ...raw, updatedAtUtc: new Date(updatedAtUtc) } : raw;
}
