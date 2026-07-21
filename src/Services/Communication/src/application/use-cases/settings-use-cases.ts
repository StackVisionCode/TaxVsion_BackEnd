import { Result, makeError } from '../../domain/shared/result.js';
import { TenantCommunicationSettings, type TenantCommunicationSettingsSnapshot } from '../../domain/settings/tenant-communication-settings.js';
import type { SettingsRepository, LimitsRepository } from '../ports/settings-repository.js';
import {
  computeEffectiveMaxMeetingParticipants,
  isFeatureAllowed,
  type TenantCommunicationLimitsSnapshot,
} from '../../domain/settings/tenant-communication-limits.js';

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

export interface UpdateSettingsCommand {
  readonly tenantId: string;
  readonly patch: Partial<{
    chatEnabled: boolean;
    callsEnabled: boolean;
    videoCallsEnabled: boolean;
    meetingsEnabled: boolean;
    supportEnabled: boolean;
    screenshotsEnabled: boolean;
    internalGroupsEnabled: boolean;
    employeeToEmployeeChatEnabled: boolean;
    restrictCustomerChatToAssignedPreparer: boolean;
    defaultCameraOff: boolean;
    defaultMicrophoneOff: boolean;
    persistChatOnEnd: boolean;
    messageRetentionDays: number;
    recordingRetentionDays: number;
    purgeEnabled: boolean;
  }>;
}

export async function updateSettings(
  cmd: UpdateSettingsCommand,
  deps: { tenantSettings: SettingsRepository },
): Promise<Result<TenantCommunicationSettingsSnapshot>> {
  const settings = await getOrCreateSettings(cmd.tenantId, deps);
  const applyResult = settings.update(cmd.patch);
  if (!applyResult.isSuccess) return Result.fail(applyResult.error);
  await deps.tenantSettings.save(settings);
  return Result.ok(settings.toSnapshot());
}

/**
 * PlanGuard: resuelve si una accion cabe dentro del plan efectivo. Compone
 * setting (tenant admin) con limits (proyeccion). Usado en use cases sensibles
 * al plan (crear meeting con maxParticipants > plan, iniciar call si suspended, etc).
 */
export class PlanGuard {
  constructor(
    private readonly settings: SettingsRepository,
    private readonly limits: LimitsRepository,
  ) {}

  async canCreateMeeting(input: {
    tenantId: string;
    requestedMaxParticipants: number;
  }): Promise<Result<{ effectiveMax: number }>> {
    const [settings, limits] = await Promise.all([
      this.settings.findByTenantId(input.tenantId),
      this.limits.findByTenantId(input.tenantId),
    ]);
    if (limits?.isSuspended) {
      return Result.fail(makeError('Plan.Suspended', 'Subscription is suspended.'));
    }
    const planLimit = limits?.maxMeetingParticipants ?? 4;
    const settingLimit = settings?.toSnapshot().meetingsEnabled === false ? 0 : planLimit;
    const effective = computeEffectiveMaxMeetingParticipants({
      planLimit,
      settingLimit,
      isSuspended: limits?.isSuspended ?? false,
    });
    if (effective === 0) return Result.fail(makeError('Plan.MeetingsDisabled', 'Meetings not available.'));
    if (input.requestedMaxParticipants > effective) {
      return Result.fail(
        makeError('Plan.MeetingSizeExceeded', `Plan allows up to ${effective} participants.`),
      );
    }
    return Result.ok({ effectiveMax: effective });
  }

  async canUseFeature(input: {
    tenantId: string;
    feature: 'calls' | 'meetings' | 'recording' | 'support';
  }): Promise<boolean> {
    const [settings, limits] = await Promise.all([
      this.settings.findByTenantId(input.tenantId),
      this.limits.findByTenantId(input.tenantId),
    ]);
    const suspended = limits?.isSuspended ?? false;
    const sSnap = settings?.toSnapshot();
    switch (input.feature) {
      case 'calls':
        return isFeatureAllowed({
          planFlag: true,
          settingFlag: sSnap?.callsEnabled ?? true,
          isSuspended: suspended,
        });
      case 'meetings':
        return isFeatureAllowed({
          planFlag: (limits?.maxMeetingParticipants ?? 4) >= 2,
          settingFlag: sSnap?.meetingsEnabled ?? true,
          isSuspended: suspended,
        });
      case 'recording':
        return isFeatureAllowed({
          planFlag: limits?.recordingEnabled ?? false,
          settingFlag: true,
          isSuspended: suspended,
        });
      case 'support':
        return isFeatureAllowed({
          planFlag: limits?.supportEnabled ?? true,
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
  deps: { limits: LimitsRepository },
): Promise<void> {
  await deps.limits.upsert(snapshot);
}
