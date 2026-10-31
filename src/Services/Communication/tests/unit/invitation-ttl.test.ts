import { describe, expect, it } from 'vitest';
import { invitationTtlSeconds } from '../../src/application/use-cases/create-meeting-invitations.js';
import { effectiveDefaults } from '../helpers/fake-effective-settings.js';

const HOUR = 3600;
const now = new Date('2026-10-29T12:00:00Z');

describe('invitationTtlSeconds (D-48)', () => {
  it('never expires before the global minimum (7 days by default)', () => {
    const ttl = invitationTtlSeconds({ now, scheduledForUtc: null, effective: effectiveDefaults() });
    expect(ttl).toBe(168 * HOUR);
  });

  it('a meeting scheduled far away expires after its end plus the grace', () => {
    const inTenDays = new Date(now.getTime() + 10 * 24 * HOUR * 1000);
    const ttl = invitationTtlSeconds({ now, scheduledForUtc: inTenDays, effective: effectiveDefaults() });
    // 10 días + 60 min de reunión + 24 h de gracia.
    expect(ttl).toBe(10 * 24 * HOUR + 60 * 60 + 24 * HOUR);
  });

  it('follows the platform values instead of constants', () => {
    const effective = effectiveDefaults(undefined, { invitationMinTtlHours: 2, invitationExpiryGraceHours: 1 });
    const ttl = invitationTtlSeconds({ now, scheduledForUtc: null, effective });
    // max(2 h mínimo, 60 min de reunión + 1 h de gracia) = 2 h.
    expect(ttl).toBe(2 * HOUR);
  });
});
