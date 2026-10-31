import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TECHNICAL_LIMITS } from '../../src/domain/settings/technical-limits.js';

describe('PlatformCommunicationSettings', () => {
  it('defaults match the owner decisions (60 min, 10 participants, mesh 4, grace 30 s, groups on)', () => {
    const s = PlatformCommunicationSettings.defaults().toSnapshot();
    expect(s.version).toBe(1);
    expect(s.maxMeetingDurationMinutes).toBe(60);
    expect(s.defaultMaxParticipants).toBe(10);
    expect(s.meshMaxParticipants).toBe(2);
    expect(s.disconnectGraceSeconds).toBe(30);
    expect(s.recordingConsentTimeoutMeetingSeconds).toBe(30);
    expect(s.recordingConsentTimeoutCallSeconds).toBe(15);
    expect(s.internalGroupsEnabled).toBe(true);
    expect(s.invitationMinTtlHours).toBe(168);
    expect(s.meetingSoundsPolicy).toBe('all');
  });

  it('update bumps the version, records the actor and returns only real changes', () => {
    const s = PlatformCommunicationSettings.defaults();
    const actor = randomUUID();
    const r = s.update({ disconnectGraceSeconds: 45, meshMaxParticipants: 2 }, { userId: actor });
    expect(r.isSuccess).toBe(true);
    if (!r.isSuccess) return;
    expect(r.value).toEqual([{ key: 'disconnectGraceSeconds', oldValue: '30', newValue: '45' }]);
    expect(s.version).toBe(2);
    expect(s.toSnapshot().updatedByUserId).toBe(actor);
  });

  it('a no-op patch does not bump the version', () => {
    const s = PlatformCommunicationSettings.defaults();
    const r = s.update({ disconnectGraceSeconds: 30 }, { userId: randomUUID() });
    expect(r.isSuccess && r.value.length).toBe(0);
    expect(s.version).toBe(1);
  });

  it('rejects values outside the technical range with the key in the message', () => {
    const s = PlatformCommunicationSettings.defaults();
    const r = s.update({ maxRecordingMinutesMeeting: TECHNICAL_LIMITS.maxRecordingMinutes.max + 1 }, { userId: randomUUID() });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Settings.OutOfTechnicalRange');
    expect(r.error.message).toContain('maxRecordingMinutesMeeting');
    expect(s.version).toBe(1);
  });

  it('rejects a default duration above the max and bad enums', () => {
    const s = PlatformCommunicationSettings.defaults();
    const actor = { userId: randomUUID() };
    expect(s.update({ maxMeetingDurationMinutes: 30, defaultMeetingDurationMinutes: 45 }, actor).isSuccess).toBe(false);
    const badPolicy = s.update({ meetingSoundsPolicy: 'loud' as 'all' }, actor);
    expect(!badPolicy.isSuccess && badPolicy.error.code).toBe('Settings.InvalidSoundsPolicy');
    const badLang = s.update({ transcriptDefaultLanguage: 'english' }, actor);
    expect(!badLang.isSuccess && badLang.error.code).toBe('Settings.InvalidLanguage');
  });

  it('rehydrate is fail-safe: corrupt values fall back and are reported', () => {
    const corrupt = { ...PlatformCommunicationSettings.defaults().toSnapshot(), disconnectGraceSeconds: 9999, meetingSoundsPolicy: 'x' as 'all' };
    const { settings, corrected } = PlatformCommunicationSettings.rehydrate(corrupt);
    expect(corrected).toEqual(['disconnectGraceSeconds', 'meetingSoundsPolicy']);
    expect(settings.toSnapshot().disconnectGraceSeconds).toBe(TECHNICAL_LIMITS.disconnectGraceSeconds.fallback);
    expect(settings.toSnapshot().meetingSoundsPolicy).toBe('all');
  });
  it('F10: analytics time zone and retention — UTC/730 by default, history purge off (null)', () => {
    const s = PlatformCommunicationSettings.defaults().toSnapshot();
    expect([s.analyticsTimeZone, s.analyticsRetentionDays, s.callsRetentionDays, s.meetingsRetentionDays, s.invitationsRetentionDays]).toEqual([
      'UTC',
      730,
      null,
      null,
      null,
    ]);
  });

  it('F10: rejects a non-IANA time zone and out-of-range history retention, accepts null to turn purge off', () => {
    const s = PlatformCommunicationSettings.defaults();
    const zone = s.update({ analyticsTimeZone: 'Mars/Olympus' }, { userId: randomUUID() });
    expect(zone.isSuccess ? 'ok' : zone.error.code).toBe('Settings.InvalidTimeZone');
    const tooShort = s.update({ meetingsRetentionDays: 5 }, { userId: randomUUID() });
    expect(tooShort.isSuccess ? 'ok' : tooShort.error.code).toBe('Settings.OutOfTechnicalRange');
    const on = s.update({ meetingsRetentionDays: 365, analyticsTimeZone: 'America/New_York' }, { userId: randomUUID() });
    expect(on.isSuccess).toBe(true);
    const off = s.update({ meetingsRetentionDays: null }, { userId: randomUUID() });
    expect(off.isSuccess && off.value).toEqual([{ key: 'meetingsRetentionDays', oldValue: '365', newValue: null }]);
  });

  it('F10: rehydrate turns a corrupt history retention off instead of purging with it', () => {
    const corrupt = { ...PlatformCommunicationSettings.defaults().toSnapshot(), callsRetentionDays: 2, analyticsTimeZone: 'nope' };
    const { settings, corrected } = PlatformCommunicationSettings.rehydrate(corrupt);
    expect(settings.toSnapshot().callsRetentionDays).toBeNull();
    expect(settings.toSnapshot().analyticsTimeZone).toBe('UTC');
    expect(corrected).toEqual(expect.arrayContaining(['callsRetentionDays', 'analyticsTimeZone']));
  });
});
