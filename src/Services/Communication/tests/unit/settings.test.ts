import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import {
  computeEffectiveMaxMeetingParticipants,
  isFeatureAllowed,
} from '../../src/domain/settings/tenant-communication-limits.js';

describe('TenantCommunicationSettings.defaults', () => {
  it('sets safe conservative defaults', () => {
    const s = TenantCommunicationSettings.defaults(randomUUID()).toSnapshot();
    expect(s.chatEnabled).toBe(true);
    expect(s.internalGroupsEnabled).toBe(false);
    expect(s.employeeToEmployeeChatEnabled).toBe(false);
    expect(s.defaultCameraOff).toBe(true);
    expect(s.messageRetentionDays).toBe(365);
  });
});

describe('TenantCommunicationSettings.update', () => {
  it('accepts partial patch', () => {
    const s = TenantCommunicationSettings.defaults(randomUUID());
    const r = s.update({ chatEnabled: false, messageRetentionDays: 30 });
    expect(r.isSuccess).toBe(true);
    expect(s.toSnapshot().chatEnabled).toBe(false);
    expect(s.toSnapshot().messageRetentionDays).toBe(30);
  });

  it('rejects invalid retention', () => {
    const s = TenantCommunicationSettings.defaults(randomUUID());
    const r = s.update({ messageRetentionDays: 100000 });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Settings.InvalidRetention');
  });
});

describe('PlanGuard helpers', () => {
  it('effectiveMax is min(plan, setting) when both allow', () => {
    expect(
      computeEffectiveMaxMeetingParticipants({ planLimit: 10, settingLimit: 8, isSuspended: false }),
    ).toBe(8);
  });

  it('effectiveMax zero when suspended', () => {
    expect(
      computeEffectiveMaxMeetingParticipants({ planLimit: 10, settingLimit: 8, isSuspended: true }),
    ).toBe(0);
  });

  it('feature disabled if either side is off', () => {
    expect(isFeatureAllowed({ planFlag: true, settingFlag: false, isSuspended: false })).toBe(false);
    expect(isFeatureAllowed({ planFlag: false, settingFlag: true, isSuspended: false })).toBe(false);
    expect(isFeatureAllowed({ planFlag: true, settingFlag: true, isSuspended: true })).toBe(false);
    expect(isFeatureAllowed({ planFlag: true, settingFlag: true, isSuspended: false })).toBe(true);
  });
});
