import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import {
  TRANSCRIPT_PROMPT_MAX_LENGTH,
  TenantCommunicationSettings,
  officeNumericRanges,
  type OfficeRangedKey,
  type TenantCommunicationSettingsPatch,
  type TenantCommunicationSettingsSnapshot,
} from '../../domain/settings/tenant-communication-settings.js';
import {
  type EffectiveCommunicationSettings,
  resolveCeilings,
} from '../../domain/settings/effective-communication-settings.js';
import type { TenantCommunicationConstraintsSnapshot } from '../../domain/settings/tenant-communication-constraints.js';
import { ConfigurationAuditReason, ConfigurationAuditScope } from '../../domain/settings/configuration-audit.js';
import type { SettingsRepository, LimitsRepository } from '../ports/settings-repository.js';
import type { TenantCommunicationConstraintsRepository } from '../ports/tenant-communication-constraints-repository.js';
import type { PlatformCommunicationSettingsRepository } from '../ports/platform-communication-settings-repository.js';
import type { ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsCacheInvalidator } from '../ports/tenant-settings-provider.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  computeEffectiveMaxMeetingParticipants,
  isFeatureAllowed,
  type TenantCommunicationLimitsSnapshot,
} from '../../domain/settings/tenant-communication-limits.js';
import { SettingsEventTypes, type TenantSettingsChangedEvent } from '../../contracts/events/settings-events.js';
import { SettingsSocketEvents, type SettingsChangedDto } from '../../contracts/socket/settings-socket-events.js';
import { buildAuditEntries } from './configuration-audit-writer.js';

export async function getOrCreateSettings(
  tenantId: string,
  deps: { tenantSettings: SettingsRepository },
): Promise<TenantCommunicationSettings> {
  const existing = await deps.tenantSettings.findByTenantId(tenantId);
  if (existing) return existing;
  const created = TenantCommunicationSettings.defaults(tenantId);
  await deps.tenantSettings.save(created);
  return created;
}

/** Lo que ve el Tenant Admin: sus ajustes, el plan, los techos de PlatformAdmin y lo vigente. */
export interface TenantSettingsOverview {
  readonly settings: TenantCommunicationSettingsSnapshot;
  readonly limits: TenantCommunicationLimitsSnapshot | null;
  readonly constraints: TenantCommunicationConstraintsSnapshot | null;
  readonly effective: EffectiveCommunicationSettings;
  /** F9: rangos técnicos de cada número editable y largo máximo del vocabulario del transcript. */
  readonly ranges: Readonly<Record<OfficeRangedKey, { readonly min: number; readonly max: number }>>;
  readonly transcriptPromptMaxLength: number;
}

export interface TenantSettingsDeps {
  readonly tenantSettings: SettingsRepository;
  readonly limits: LimitsRepository;
  readonly tenantConstraints: TenantCommunicationConstraintsRepository;
  readonly platformSettings: PlatformCommunicationSettingsRepository;
  readonly configurationAudit: ConfigurationAuditRepository;
  readonly effectiveSettings: EffectiveSettingsResolver;
  readonly settingsCache: TenantSettingsCacheInvalidator;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter?: RealtimeEmitter | undefined;
}

export async function getSettingsOverview(tenantId: string, deps: TenantSettingsDeps): Promise<TenantSettingsOverview> {
  const [settings, limits, constraints, effective] = await Promise.all([
    getOrCreateSettings(tenantId, deps),
    deps.limits.findByTenantId(tenantId),
    deps.tenantConstraints.findByTenantId(tenantId),
    deps.effectiveSettings.resolve(tenantId),
  ]);
  return {
    settings: settings.toSnapshot(),
    limits,
    constraints: constraints ? constraints.toSnapshot() : null,
    effective,
    ranges: officeNumericRanges(),
    transcriptPromptMaxLength: TRANSCRIPT_PROMPT_MAX_LENGTH,
  };
}

export interface UpdateSettingsCommand {
  readonly tenantId: string;
  readonly actorUserId: string;
  readonly correlationId: string;
  readonly patch: TenantCommunicationSettingsPatch;
}

/**
 * Tenant Admin ajusta su oficina. Orden: forma (rangos técnicos) → techo efectivo (T-46) →
 * persistir → auditar → invalidar cachés al instante (T-13) → evento + aviso realtime.
 */
export async function updateSettings(
  cmd: UpdateSettingsCommand,
  deps: TenantSettingsDeps,
): Promise<Result<TenantSettingsOverview>> {
  const settings = await getOrCreateSettings(cmd.tenantId, deps);
  const [platform, limits, constraints] = await Promise.all([
    deps.platformSettings.get(),
    deps.limits.findByTenantId(cmd.tenantId),
    deps.tenantConstraints.findByTenantId(cmd.tenantId),
  ]);
  const ceilings = resolveCeilings({
    platform: platform.toSnapshot(),
    limits,
    constraints: constraints ? constraints.toSnapshot() : null,
  });
  const withinCeilings = TenantCommunicationSettings.validateAgainstCeilings(cmd.patch, ceilings);
  if (!withinCeilings.isSuccess) return Result.fail(withinCeilings.error);

  const now = new Date();
  const applied = settings.update(cmd.patch, now);
  if (!applied.isSuccess) return Result.fail(applied.error);
  const changes = applied.value;
  if (changes.length === 0) return Result.ok(await getSettingsOverview(cmd.tenantId, deps));

  await deps.tenantSettings.save(settings);
  await deps.configurationAudit.append(
    buildAuditEntries({
      tenantId: cmd.tenantId,
      scope: ConfigurationAuditScope.TenantSettings,
      reason: ConfigurationAuditReason.Update,
      actorUserId: cmd.actorUserId,
      correlationId: cmd.correlationId,
      changes,
      now,
    }),
  );
  await Promise.all([deps.settingsCache.invalidate(cmd.tenantId), deps.effectiveSettings.invalidateTenant(cmd.tenantId)]);

  const event: TenantSettingsChangedEvent = {
    eventId: randomUUID(),
    eventType: SettingsEventTypes.TenantSettingsChanged,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    actorUserId: cmd.actorUserId,
    reason: 'Update',
    changes,
  };
  await deps.publisher.enqueue(event);

  const payload: SettingsChangedDto = { scope: 'tenant', changedKeys: changes.map((c) => c.key) };
  deps.emitter?.emitToTenantMembers({
    tenantId: cmd.tenantId,
    event: SettingsSocketEvents.Changed,
    envelope: { eventId: randomUUID(), correlationId: cmd.correlationId, emittedAtUtc: now.toISOString(), payload },
  });

  return Result.ok(await getSettingsOverview(cmd.tenantId, deps));
}

/**
 * PlanGuard: resuelve si una accion cabe dentro del plan efectivo. Compone
 * setting (tenant admin) con limits (proyeccion). Usado en use cases sensibles
 * al plan (crear meeting con maxParticipants > plan, iniciar call si suspended, etc).
 * El cupo de participantes sale del resolver (global → plan → techo → oficina), no de un default.
 */
export class PlanGuard {
  constructor(
    private readonly settings: SettingsRepository,
    private readonly limits: LimitsRepository,
    private readonly effectiveSettings: EffectiveSettingsResolver,
  ) {}

  async canCreateMeeting(input: {
    tenantId: string;
    requestedMaxParticipants: number;
  }): Promise<Result<{ effectiveMax: number }>> {
    const [settings, limits, effective] = await Promise.all([
      this.settings.findByTenantId(input.tenantId),
      this.limits.findByTenantId(input.tenantId),
      this.effectiveSettings.resolve(input.tenantId),
    ]);
    if (limits?.isSuspended) {
      return Result.fail(makeError('Plan.Suspended', 'Subscription is suspended.'));
    }
    const planLimit = effective.meeting.maxParticipants;
    const settingLimit = settings?.toSnapshot().meetingsEnabled === false ? 0 : planLimit;
    const effectiveMax = computeEffectiveMaxMeetingParticipants({
      planLimit,
      settingLimit,
      isSuspended: limits?.isSuspended ?? false,
    });
    if (effectiveMax === 0) return Result.fail(makeError('Plan.MeetingsDisabled', 'Meetings not available.'));
    if (input.requestedMaxParticipants > effectiveMax) {
      return Result.fail(
        makeError('Plan.MeetingSizeExceeded', `Plan allows up to ${effectiveMax} participants.`),
      );
    }
    return Result.ok({ effectiveMax });
  }

  async canUseFeature(input: {
    tenantId: string;
    feature: 'calls' | 'meetings' | 'recording' | 'support';
  }): Promise<boolean> {
    const [settings, limits, effective] = await Promise.all([
      this.settings.findByTenantId(input.tenantId),
      this.limits.findByTenantId(input.tenantId),
      this.effectiveSettings.resolve(input.tenantId),
    ]);
    const suspended = limits?.isSuspended ?? false;
    const sSnap = settings?.toSnapshot();
    switch (input.feature) {
      case 'calls':
        return isFeatureAllowed({
          planFlag: effective.ceilings.callsAllowed,
          settingFlag: sSnap?.callsEnabled ?? true,
          isSuspended: suspended,
        });
      case 'meetings':
        return isFeatureAllowed({
          planFlag: effective.meeting.maxParticipants >= 2,
          settingFlag: sSnap?.meetingsEnabled ?? true,
          isSuspended: suspended,
        });
      case 'recording':
        return isFeatureAllowed({
          planFlag: effective.ceilings.recordingAllowed,
          settingFlag: true,
          isSuspended: suspended,
        });
      case 'support':
        return isFeatureAllowed({
          planFlag: effective.ceilings.supportAllowed,
          settingFlag: sSnap?.supportEnabled ?? true,
          isSuspended: suspended,
        });
    }
  }
}

/**
 * Aplica el snapshot vinculado a un evento Subscription. Es el shape comun a los
 * consumers Activated/PlanChanged/Suspended/SeatsPurchased.
 */
export async function applyLimitsUpdate(
  snapshot: TenantCommunicationLimitsSnapshot,
  deps: {
    limits: LimitsRepository;
    planCodeCache?: { invalidate(tenantId: string): void };
    modulesCache?: { invalidate(tenantId: string): void };
    /** El plan cambió: los valores efectivos de la oficina se recomputan en la próxima lectura. */
    effectiveSettings?: Pick<EffectiveSettingsResolver, 'invalidateTenant'>;
  },
): Promise<void> {
  await deps.limits.upsert(snapshot);
  // RateLimit Fase 6 (port a Node) — invalida al vuelo en vez de esperar el TTL de 5 min del
  // cache de planCode, mismo criterio que CachedTenantPlanCodeReader.InvalidateAsync (.NET).
  deps.planCodeCache?.invalidate(snapshot.tenantId);
  // Gate de modulo — mismo criterio para el cache de modulos habilitados.
  deps.modulesCache?.invalidate(snapshot.tenantId);
  await deps.effectiveSettings?.invalidateTenant(snapshot.tenantId);
}
