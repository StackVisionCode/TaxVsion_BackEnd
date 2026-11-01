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
    expect(s.guestsAllowed).toBe(true);
    expect(s.maxMeetingParticipants).toBeNull();
    expect(s.employeeToEmployeeChatEnabled).toBe(false);
    expect(s.defaultCameraOff).toBe(true);
    // La retención ya no es de la oficina: la fija la plataforma (global o excepción por oficina).
    expect('messageRetentionDays' in s).toBe(false);
  });
});

describe('TenantCommunicationSettings.update', () => {
  it('accepts partial patch', () => {
    const s = TenantCommunicationSettings.defaults(randomUUID());
    const r = s.update({ chatEnabled: false });
    expect(r.isSuccess).toBe(true);
    expect(s.toSnapshot().chatEnabled).toBe(false);
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
