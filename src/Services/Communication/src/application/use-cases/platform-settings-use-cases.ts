import { randomUUID } from 'node:crypto';
import { Result } from '../../domain/shared/result.js';
import {
  type PlatformCommunicationSettingsPatch,
  type PlatformCommunicationSettingsSnapshot,
} from '../../domain/settings/platform-communication-settings.js';
import { ConfigurationAuditReason, ConfigurationAuditScope } from '../../domain/settings/configuration-audit.js';
import { TECHNICAL_LIMITS } from '../../domain/settings/technical-limits.js';
import type { PlatformCommunicationSettingsRepository } from '../ports/platform-communication-settings-repository.js';
import type { ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PlatformTenantProvider } from '../ports/platform-tenant-provider.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { SettingsEventTypes, type PlatformSettingsChangedEvent } from '../../contracts/events/settings-events.js';
import { SettingsSocketEvents, type SettingsChangedDto } from '../../contracts/socket/settings-socket-events.js';
import { buildAuditEntries } from './configuration-audit-writer.js';

export interface PlatformSettingsView {
  readonly settings: PlatformCommunicationSettingsSnapshot;
  /** Rangos técnicos, para que el cliente del API sepa qué acepta cada clave. */
  readonly technicalLimits: typeof TECHNICAL_LIMITS;
}

export interface PlatformSettingsDeps {
  readonly platformSettings: PlatformCommunicationSettingsRepository;
  readonly configurationAudit: ConfigurationAuditRepository;
  readonly effectiveSettings: EffectiveSettingsResolver;
  readonly publisher: IntegrationEventPublisher;
  readonly platform: PlatformTenantProvider;
  readonly emitter?: RealtimeEmitter | undefined;
}

export async function getPlatformSettings(deps: Pick<PlatformSettingsDeps, 'platformSettings'>): Promise<PlatformSettingsView> {
  const settings = await deps.platformSettings.get();
  return { settings: settings.toSnapshot(), technicalLimits: TECHNICAL_LIMITS };
}

export interface UpdatePlatformSettingsCommand {
  readonly actorUserId: string;
  readonly correlationId: string;
  readonly patch: PlatformCommunicationSettingsPatch;
}

/**
 * Cambia valores globales: aggregate → persistir → auditar → invalidar caché → evento de
 * integración → aviso realtime a toda la flota (cada oficina refresca sus settings).
 */
export async function updatePlatformSettings(
  cmd: UpdatePlatformSettingsCommand,
  deps: PlatformSettingsDeps,
): Promise<Result<PlatformSettingsView>> {
  const settings = await deps.platformSettings.get();
  const now = new Date();
  const applied = settings.update(cmd.patch, { userId: cmd.actorUserId, now });
  if (!applied.isSuccess) return Result.fail(applied.error);
  const changes = applied.value;
  if (changes.length === 0) {
    return Result.ok({ settings: settings.toSnapshot(), technicalLimits: TECHNICAL_LIMITS });
  }

  await deps.platformSettings.save(settings);
  await deps.configurationAudit.append(
    buildAuditEntries({
      tenantId: null,
      scope: ConfigurationAuditScope.Platform,
      reason: ConfigurationAuditReason.Update,
      actorUserId: cmd.actorUserId,
      correlationId: cmd.correlationId,
      changes,
      now,
    }),
  );
  await deps.effectiveSettings.invalidatePlatform();

  const event: PlatformSettingsChangedEvent = {
    eventId: randomUUID(),
    eventType: SettingsEventTypes.PlatformSettingsChanged,
    tenantId: deps.platform.getPlatformTenantId(),
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    version: settings.version,
    actorUserId: cmd.actorUserId,
    changes,
  };
  await deps.publisher.enqueue(event);

  const payload: SettingsChangedDto = { scope: 'platform', changedKeys: changes.map((c) => c.key) };
  deps.emitter?.emitBroadcast({
    event: SettingsSocketEvents.Changed,
    envelope: { eventId: randomUUID(), correlationId: cmd.correlationId, emittedAtUtc: now.toISOString(), payload },
  });

  return Result.ok({ settings: settings.toSnapshot(), technicalLimits: TECHNICAL_LIMITS });
}
