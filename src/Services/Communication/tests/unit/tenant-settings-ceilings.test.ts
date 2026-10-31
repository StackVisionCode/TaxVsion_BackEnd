import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationConstraints } from '../../src/domain/settings/tenant-communication-constraints.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { resolveCeilings } from '../../src/domain/settings/effective-communication-settings.js';

const platform = PlatformCommunicationSettings.defaults().toSnapshot();
const ceilings = resolveCeilings({ platform, limits: null, constraints: null });

describe('TenantCommunicationSettings against ceilings', () => {
  it('validateAgainstCeilings rejects any key above its ceiling (T-46)', () => {
    const r = TenantCommunicationSettings.validateAgainstCeilings({ maxMeetingParticipants: 11 }, ceilings);
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Settings.AboveCeiling');
    expect(r.error.message).toContain('maxMeetingParticipants');
  });

  it('validateAgainstCeilings accepts null (inherit) and values at the ceiling', () => {
    expect(TenantCommunicationSettings.validateAgainstCeilings({ maxMeetingParticipants: null }, ceilings).isSuccess).toBe(true);
    expect(TenantCommunicationSettings.validateAgainstCeilings({ maxMeetingParticipants: 10, defaultMeetingDurationMinutes: 60 }, ceilings).isSuccess).toBe(true);
  });

  it('guests cannot be enabled by the office when the platform forbids them', () => {
    const noGuests = resolveCeilings({ platform: { ...platform, guestsAllowed: false }, limits: null, constraints: null });
    expect(TenantCommunicationSettings.validateAgainstCeilings({ guestsAllowed: true }, noGuests).isSuccess).toBe(false);
    expect(TenantCommunicationSettings.validateAgainstCeilings({ guestsAllowed: false }, noGuests).isSuccess).toBe(true);
  });

  it('applyCeilings lowers only what is above the new ceiling and reports it (auto-correct)', () => {
    const settings = TenantCommunicationSettings.defaults(randomUUID());
    settings.update({ maxMeetingParticipants: 10, maxRecordingMinutesMeeting: 20, guestsAllowed: true });
    const constraints = TenantCommunicationConstraints.empty(settings.tenantId);
    constraints.update({ maxMeetingParticipants: 6, guestsAllowed: false }, { userId: randomUUID() });
    const lowered = resolveCeilings({ platform, limits: null, constraints: constraints.toSnapshot() });

    const corrections = settings.applyCeilings(lowered);
    expect(corrections).toEqual([
      { key: 'maxMeetingParticipants', oldValue: '10', newValue: '6' },
      { key: 'guestsAllowed', oldValue: 'true', newValue: 'false' },
    ]);
    expect(settings.toSnapshot().maxRecordingMinutesMeeting).toBe(20);
    expect(settings.applyCeilings(lowered)).toEqual([]);
  });

  it('update still validates technical ranges on the new keys', () => {
    const settings = TenantCommunicationSettings.defaults(randomUUID());
    const r = settings.update({ ringingTimeoutSeconds: 5 });
    expect(!r.isSuccess && r.error.code).toBe('Settings.OutOfTechnicalRange');
    expect(settings.update({ meetingSoundsPolicy: 'hostOnly', transcriptLanguage: 'es' }).isSuccess).toBe(true);
  });
});

describe('TenantCommunicationConstraints', () => {
  it('starts empty, validates ranges and versions on change', () => {
    const c = TenantCommunicationConstraints.empty(randomUUID());
    expect(c.toSnapshot().maxMeetingParticipants).toBeNull();
    const bad = c.update({ maxMeetingParticipants: 1 }, { userId: randomUUID() });
    expect(!bad.isSuccess && bad.error.code).toBe('Settings.OutOfTechnicalRange');
    const ok = c.update({ maxMeetingParticipants: 5, notes: 'pilot office' }, { userId: randomUUID() });
    expect(ok.isSuccess && ok.value.map((x) => x.key)).toEqual(['maxMeetingParticipants', 'notes']);
    expect(c.toSnapshot().version).toBe(2);
  });
});
