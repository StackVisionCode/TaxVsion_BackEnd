import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings, validateQualityOrdering } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { UserCommunicationPreferences } from '../../src/domain/settings/user-communication-preferences.js';
import { resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';
import { toMediaPolicyDto } from '../../src/application/use-cases/media-policy-query.js';
import { TECHNICAL_LIMITS } from '../../src/domain/settings/technical-limits.js';

const actor = { userId: randomUUID() };

describe('media policy settings (F4)', () => {
  it('platform defaults carry the media policy and reject disordered quality thresholds', () => {
    const platform = PlatformCommunicationSettings.defaults();
    const s = platform.toSnapshot();
    expect([s.qualityExcellentRttMs, s.qualityGoodRttMs, s.qualityUnstableRttMs]).toEqual([150, 300, 500]);
    expect(s.cameraMaxWidth).toBe(TECHNICAL_LIMITS.cameraMaxWidth.fallback);
    expect(platform.update({ qualityGoodRttMs: 100 }, actor).isSuccess).toBe(false);
    expect(platform.update({ qualityGoodRttMs: 100 }, actor)).toMatchObject({ isSuccess: false, error: { code: 'Settings.QualityThresholdsOrder' } });
    expect(platform.update({ qualityGoodRttMs: 250, screenMaxFps: 20 }, actor).isSuccess).toBe(true);
    expect(platform.update({ screenMaxFps: 999 }, actor)).toMatchObject({ isSuccess: false, error: { code: 'Settings.OutOfTechnicalRange' } });
    expect(validateQualityOrdering({ ...s, qualityUnstableLossPct: 1 }).isSuccess).toBe(false);
  });

  it('resolves platform > office > user and the DTO applies the user audio processing on top of the office default', () => {
    const tenantId = randomUUID();
    const platform = PlatformCommunicationSettings.defaults();
    platform.update({ clientsCanShareScreen: true, screenAudioAllowed: true, echoMuteOnDetect: false }, actor);
    const tenant = TenantCommunicationSettings.defaults(tenantId);
    tenant.update({ clientsCanShareScreen: false, audioNoiseSuppressionDefault: false, echoMuteOnDetect: true });
    const prefs = UserCommunicationPreferences.defaults(tenantId, actor.userId);
    prefs.update({ noiseSuppression: true, joinMuted: true, meetingSoundsMuted: true });

    const effective = resolveEffectiveSettings({ platform: platform.toSnapshot(), limits: null, constraints: null, tenant: tenant.toSnapshot(), preferences: prefs.toSnapshot() });
    expect(effective.media.screenShare).toEqual({ clientsCanShare: false, audioAllowed: true });
    expect(effective.media.audio.processing.noiseSuppression).toBe(false);
    expect(effective.media.echo.muteOnEcho).toBe(true);

    const dto = toMediaPolicyDto(effective);
    expect(dto.audio.processing.noiseSuppression).toBe(true);
    expect(dto.audio.processing.echoCancellation).toBe(true);
    expect(dto.preferences.joinMuted).toBe(true);
    expect(dto.sounds).toMatchObject({ policy: 'all', volumePct: 60, repeatGapMs: 400, muted: true });
    expect(dto.quality.thresholds.unstable.jitterMs).toBe(100);
    expect(dto.version).toBe(effective.platformVersion);
  });

  it('an office cannot enable what the platform forbids', () => {
    const platform = PlatformCommunicationSettings.defaults();
    platform.update({ screenAudioAllowed: false }, actor);
    const tenant = TenantCommunicationSettings.defaults(randomUUID());
    tenant.update({ screenAudioAllowed: true });
    const effective = resolveEffectiveSettings({ platform: platform.toSnapshot(), limits: null, constraints: null, tenant: tenant.toSnapshot() });
    expect(effective.media.screenShare.audioAllowed).toBe(false);
    expect(toMediaPolicyDto(effective).preferences.audioProcessing).toEqual({ echoCancellation: null, noiseSuppression: null, autoGainControl: null });
  });
});
