import { describe, expect, it } from 'vitest';
import { Result, makeError } from '../../src/domain/shared/result.js';
import { TenantId, UserId } from '../../src/domain/shared/ids.js';
import { hasPermission, CommunicationPermissions } from '../../src/domain/shared/permissions.js';
import { normalizeCorrelationId } from '../../src/domain/shared/correlation.js';

describe('Result', () => {
  it('ok wraps a value and flags success', () => {
    const r = Result.ok(42);
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value).toBe(42);
  });

  it('fail wraps an error and flags failure', () => {
    const r = Result.fail(makeError('X.Test', 'boom'));
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('X.Test');
  });
});

describe('branded ids', () => {
  it('parses lowercase UUID and rejects invalid input', () => {
    const id = TenantId('8f58a521-4c25-4d91-9f4e-7ad5df14c001');
    expect(id).toBe('8f58a521-4c25-4d91-9f4e-7ad5df14c001');
    expect(() => TenantId('not-a-uuid')).toThrow();
  });

  it('normalizes uppercase to lowercase (kills legacy case bug)', () => {
    const id = UserId('AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE');
    expect(id).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });
});

describe('permissions', () => {
  it('TenantAdmin bypasses catalog', () => {
    expect(hasPermission('TenantAdmin', [], CommunicationPermissions.MeetingHost)).toBe(true);
  });

  it('regular actor needs the exact permission', () => {
    expect(hasPermission('TenantEmployee', [], CommunicationPermissions.ChatStart)).toBe(false);
    expect(
      hasPermission('TenantEmployee', ['communication.chat.start'], CommunicationPermissions.ChatStart),
    ).toBe(true);
  });
});

describe('correlationId', () => {
  it('normalizes a valid id', () => {
    expect(normalizeCorrelationId('taxvision-check-001')).toBe('taxvision-check-001');
  });

  it('replaces invalid id with generated one', () => {
    const bad = 'bad space \n';
    const out = normalizeCorrelationId(bad);
    expect(out).not.toBe(bad);
    expect(out.length).toBeGreaterThan(0);
  });
});
