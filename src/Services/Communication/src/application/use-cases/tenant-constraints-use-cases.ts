import { randomUUID } from 'node:crypto';
import { Result } from '../../domain/shared/result.js';
import {
  TenantCommunicationConstraints,
  type TenantCommunicationConstraintsPatch,
  type TenantCommunicationConstraintsSnapshot,
} from '../../domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../domain/settings/tenant-communication-settings.js';
import { resolveCeilings, type EffectiveCeilings } from '../../domain/settings/effective-communication-settings.js';
import { ConfigurationAuditReason, ConfigurationAuditScope } from '../../domain/settings/configuration-audit.js';
import type { ConfigurationChange } from '../../domain/settings/configuration-change.js';
import type { TenantCommunicationConstraintsRepository } from '../ports/tenant-communication-constraints-repository.js';
import type { PlatformCommunicationSettingsRepository } from '../ports/platform-communication-settings-repository.js';
import type { LimitsRepository, SettingsRepository } from '../ports/settings-repository.js';
import type { ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsCacheInvalidator } from '../ports/tenant-settings-provider.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  SettingsEventTypes,
  type TenantConstraintsChangedEvent,
  type TenantSettingsChangedEvent,
} from '../../contracts/events/settings-events.js';
import { SettingsSocketEvents, type SettingsChangedDto } from '../../contracts/socket/settings-socket-events.js';
import { buildAuditEntries } from './configuration-audit-writer.js';

export interface TenantConstraintsView {
  readonly constraints: TenantCommunicationConstraintsSnapshot;
  /** Techos que resultan tras combinar global, plan y estos techos. */
  readonly ceilings: EffectiveCeilings;
}

export interface TenantConstraintsDeps {
  readonly tenantConstraints: TenantCommunicationConstraintsRepository;
  readonly platformSettings: PlatformCommunicationSettingsRepository;
  readonly limits: LimitsRepository;
  readonly tenantSettings: SettingsRepository;
  readonly configurationAudit: ConfigurationAuditRepository;
  readonly effectiveSettings: EffectiveSettingsResolver;
  readonly settingsCache: TenantSettingsCacheInvalidator;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter?: RealtimeEmitter | undefined;
}

export async function getTenantConstraints(
  tenantId: string,
  deps: Pick<TenantConstraintsDeps, 'tenantConstraints' | 'platformSettings' | 'limits'>,
): Promise<TenantConstraintsView> {
  const constraints =
    (await deps.tenantConstraints.findByTenantId(tenantId)) ?? TenantCommunicationConstraints.empty(tenantId);
  const [platform, limits] = await Promise.all([deps.platformSettings.get(), deps.limits.findByTenantId(tenantId)]);
  const snapshot = constraints.toSnapshot();
  return {
    constraints: snapshot,
    ceilings: resolveCeilings({ platform: platform.toSnapshot(), limits, constraints: snapshot }),
  };
}

export interface UpdateTenantConstraintsCommand {
  readonly tenantId: string;
  readonly actorUserId: string;
  readonly correlationId: string;
  readonly patch: TenantCommunicationConstraintsPatch;
}

export interface UpdateTenantConstraintsResult extends TenantConstraintsView {
  /** Ajustes de la oficina que quedaron por encima del techo nuevo y se bajaron solos (ADD-09 H-4). */
  readonly autoCorrected: readonly ConfigurationChange[];
}

/**
 * PlatformAdmin fija techos para una oficina. Si la oficina ya tenía un valor por encima del techo
 * nuevo, se auto-corrige y queda en la auditoría con motivo AutoCorrect (patrón Signature).
 */
export async function updateTenantConstraints(
  cmd: UpdateTenantConstraintsCommand,
  deps: TenantConstraintsDeps,
): Promise<Result<UpdateTenantConstraintsResult>> {
  const constraints =
    (await deps.tenantConstraints.findByTenantId(cmd.tenantId)) ?? TenantCommunicationConstraints.empty(cmd.tenantId);
  const now = new Date();
  const applied = constraints.update(cmd.patch, { userId: cmd.actorUserId, now });
  if (!applied.isSuccess) return Result.fail(applied.error);
  const changes = applied.value;

  const [platform, limits] = await Promise.all([deps.platformSettings.get(), deps.limits.findByTenantId(cmd.tenantId)]);
  const ceilings = resolveCeilings({ platform: platform.toSnapshot(), limits, constraints: constraints.toSnapshot() });
  if (changes.length === 0) {
    return Result.ok({ constraints: constraints.toSnapshot(), ceilings, autoCorrected: [] });
  }

  await deps.tenantConstraints.save(constraints);
  const autoCorrected = await autoCorrectTenantSettings(cmd, ceilings, now, deps);

  await deps.configurationAudit.append(
    buildAuditEntries({
      tenantId: cmd.tenantId,
      scope: ConfigurationAuditScope.TenantConstraints,
      reason: ConfigurationAuditReason.Update,
      actorUserId: cmd.actorUserId,
      correlationId: cmd.correlationId,
      changes,
      now,
    }),
  );
  await deps.effectiveSettings.invalidateTenant(cmd.tenantId);

  const event: TenantConstraintsChangedEvent = {
    eventId: randomUUID(),
    eventType: SettingsEventTypes.TenantConstraintsChanged,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    actorUserId: cmd.actorUserId,
    changes,
  };
  await deps.publisher.enqueue(event);
  notifyTenant(cmd, [...changes, ...autoCorrected], now, deps.emitter);

  return Result.ok({ constraints: constraints.toSnapshot(), ceilings, autoCorrected });
}

async function autoCorrectTenantSettings(
  cmd: UpdateTenantConstraintsCommand,
  ceilings: EffectiveCeilings,
  now: Date,
  deps: TenantConstraintsDeps,
): Promise<ConfigurationChange[]> {
  const settings =
    (await deps.tenantSettings.findByTenantId(cmd.tenantId)) ?? TenantCommunicationSettings.defaults(cmd.tenantId, now);
  const corrected = settings.applyCeilings(ceilings, now);
  if (corrected.length === 0) return [];

  await deps.tenantSettings.save(settings);
  await deps.settingsCache.invalidate(cmd.tenantId);
  await deps.configurationAudit.append(
    buildAuditEntries({
      tenantId: cmd.tenantId,
      scope: ConfigurationAuditScope.TenantSettings,
      reason: ConfigurationAuditReason.AutoCorrect,
      actorUserId: cmd.actorUserId,
      correlationId: cmd.correlationId,
      changes: corrected,
      now,
    }),
  );
  const event: TenantSettingsChangedEvent = {
    eventId: randomUUID(),
    eventType: SettingsEventTypes.TenantSettingsChanged,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    actorUserId: null,
    reason: 'AutoCorrect',
    changes: corrected,
  };
  await deps.publisher.enqueue(event);
  return corrected;
}

function notifyTenant(
  cmd: UpdateTenantConstraintsCommand,
  changes: readonly ConfigurationChange[],
  now: Date,
  emitter: RealtimeEmitter | undefined,
): void {
  if (!emitter) return;
  const payload: SettingsChangedDto = { scope: 'tenant', changedKeys: changes.map((c) => c.key) };
  emitter.emitToTenantMembers({
    tenantId: cmd.tenantId,
    event: SettingsSocketEvents.Changed,
    envelope: { eventId: randomUUID(), correlationId: cmd.correlationId, emittedAtUtc: now.toISOString(), payload },
  });
}
