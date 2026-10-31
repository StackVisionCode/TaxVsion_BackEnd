import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationConstraints } from '../../src/domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { UserCommunicationPreferences } from '../../src/domain/settings/user-communication-preferences.js';
import type { TenantCommunicationLimitsSnapshot } from '../../src/domain/settings/tenant-communication-limits.js';
import { resolveCeilings, resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';
import { TECHNICAL_LIMITS } from '../../src/domain/settings/technical-limits.js';

const tenantId = randomUUID();
const platform = PlatformCommunicationSettings.defaults().toSnapshot();

function limits(patch: Partial<TenantCommunicationLimitsSnapshot> = {}): TenantCommunicationLimitsSnapshot {
  return {
    tenantId,
    planCode: 'pro',
    maxMeetingParticipants: null,
    maxMeetingMinutes: null,
    maxConcurrentCalls: null,
    maxMonthlyMinutes: null,
    recordingEnabled: null,
    supportEnabled: true,
    isSuspended: false,
    enabledModules: ['meetings'],
    maxRecordingMinutesMeeting: null,
    maxRecordingMinutesAudioCall: null,
    maxRecordingMinutesVideoCall: null,
    recordingVideoEnabled: null,
    transcriptEnabled: null,
    callsEnabled: null,
    maxCallMinutes: null,
    updatedAtUtc: new Date(),
    ...patch,
  };
}

describe('resolveCeilings', () => {
  it('without plan or constraints the platform values are the ceilings', () => {
    const c = resolveCeilings({ platform, limits: null, constraints: null });
    expect(c.maxParticipants).toBe(10);
    expect(c.maxMeetingDurationMinutes).toBe(60);
    expect(c.maxRecordingMinutesMeeting).toBe(60);
    expect(c.recordingAllowed).toBe(true);
    expect(c.guestsAllowed).toBe(true);
  });

  it('the lowest layer wins for numbers and AND for booleans', () => {
    const constraints = TenantCommunicationConstraints.empty(tenantId);
    constraints.update({ maxMeetingParticipants: 6, recordingAllowed: false }, { userId: randomUUID() });
    const c = resolveCeilings({
      platform,
      limits: limits({ maxMeetingParticipants: 8, maxMeetingMinutes: 45, recordingEnabled: true }),
      constraints: constraints.toSnapshot(),
    });
    expect(c.maxParticipants).toBe(6);
    expect(c.maxMeetingDurationMinutes).toBe(45);
    expect(c.recordingAllowed).toBe(false);
  });

  it('a plan without the key does not restrict (platform rules), but a plan that disables does', () => {
    expect(resolveCeilings({ platform, limits: limits(), constraints: null }).recordingAllowed).toBe(true);
    expect(
      resolveCeilings({ platform, limits: limits({ recordingEnabled: false }), constraints: null }).recordingAllowed,
    ).toBe(false);
  });

  it('a meeting recording never outlasts the meeting itself (C-04)', () => {
    const c = resolveCeilings({ platform, limits: limits({ maxMeetingMinutes: 30, maxRecordingMinutesMeeting: 120 }), constraints: null });
    expect(c.maxRecordingMinutesMeeting).toBe(30);
  });

  it('values are clamped to the technical range even if a layer is corrupt', () => {
    const c = resolveCeilings({ platform, limits: limits({ maxMeetingParticipants: 1 }), constraints: null });
    expect(c.maxParticipants).toBe(TECHNICAL_LIMITS.maxParticipants.min);
  });
});

describe('resolveEffectiveSettings', () => {
  it('office values apply only when they are at or below the ceiling', () => {
    const tenant = TenantCommunicationSettings.defaults(tenantId);
    tenant.update({ maxMeetingParticipants: 4, defaultMeetingDurationMinutes: 30, ringingTimeoutSeconds: 45 });
    const e = resolveEffectiveSettings({ platform, limits: limits(), constraints: null, tenant: tenant.toSnapshot() });
    expect(e.meeting.maxParticipants).toBe(4);
    expect(e.meeting.defaultDurationMinutes).toBe(30);
    expect(e.meeting.maxDurationMinutes).toBe(60);
    expect(e.calls.ringingTimeoutSeconds).toBe(45);
  });

  it('a stale office value above the ceiling is capped at read time', () => {
    const tenant = TenantCommunicationSettings.rehydrate({
      ...TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
      maxMeetingParticipants: 50,
    });
    const e = resolveEffectiveSettings({
      platform,
      limits: limits({ maxMeetingParticipants: 8 }),
      constraints: null,
      tenant: tenant.toSnapshot(),
    });
    expect(e.meeting.maxParticipants).toBe(8);
  });

  it('internal groups are a platform switch, guests need every layer, calls need plan and office', () => {
    const offPlatform = { ...platform, internalGroupsEnabled: false, guestsAllowed: true };
    const tenant = TenantCommunicationSettings.defaults(tenantId);
    tenant.update({ callsEnabled: false });
    const e = resolveEffectiveSettings({ platform: offPlatform, limits: limits(), constraints: null, tenant: tenant.toSnapshot() });
    expect(e.chat.internalGroupsEnabled).toBe(false);
    expect(e.meeting.guestsAllowed).toBe(true);
    expect(e.calls.enabled).toBe(false);
  });

  it('user preferences override only what is theirs', () => {
    const prefs = UserCommunicationPreferences.defaults(tenantId, randomUUID());
    prefs.update({ meetingSoundsMuted: true, transcriptLanguage: 'es', autoPictureInPicture: true });
    const e = resolveEffectiveSettings({
      platform,
      limits: null,
      constraints: null,
      tenant: TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
      preferences: prefs.toSnapshot(),
    });
    expect(e.user).toMatchObject({ soundsMuted: true, transcriptLanguage: 'es', preferAudioOnly: false, joinMuted: false, joinCameraOff: false, autoPictureInPicture: true });
    expect(e.user?.audioProcessing).toEqual({ echoCancellation: null, noiseSuppression: null, autoGainControl: null });
    // F4: la politica de media sale resuelta con los defaults tecnicos.
    expect(e.media.quality.thresholds.good.rttMs).toBe(300);
    expect(e.media.audio.processing.echoCancellation).toBe(true);
    expect(e.meeting.soundsPolicy).toBe('all');
    expect(e.recording.transcriptLanguage).toBe('auto');
  });

  it('carries the platform version and plan code for cache coherence', () => {
    const e = resolveEffectiveSettings({
      platform: { ...platform, version: 7 },
      limits: limits({ isSuspended: true }),
      constraints: null,
      tenant: TenantCommunicationSettings.defaults(tenantId).toSnapshot(),
    });
    expect(e.platformVersion).toBe(7);
    expect(e.planCode).toBe('pro');
    expect(e.isSuspended).toBe(true);
  });
});
