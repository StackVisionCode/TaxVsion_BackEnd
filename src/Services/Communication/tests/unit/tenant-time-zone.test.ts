import { describe, expect, it } from 'vitest';
import {
  normalizeTimeZoneId,
  projectChange,
  reconcileWithSource,
  type TenantTimeZone,
} from '../../src/domain/time-zones/tenant-time-zone.js';

const T = 'b7c1e0a2-0000-4000-8000-000000000001';

function row(overrides: Partial<TenantTimeZone> = {}): TenantTimeZone {
  return {
    tenantId: T,
    timeZoneId: 'America/New_York',
    sourceVersion: 3,
    sourceUpdatedAtUtc: new Date('2026-11-14T10:00:00Z'),
    ...overrides,
  };
}

describe('tenant time zone projection rules', () => {
  it('acepta zonas IANA y rechaza basura', () => {
    expect(normalizeTimeZoneId('  America/Chicago ')).toBe('America/Chicago');
    expect(normalizeTimeZoneId('Etc/UTC')).toBe('Etc/UTC');
    expect(normalizeTimeZoneId('Mars/Olympus')).toBeNull();
    expect(normalizeTimeZoneId('')).toBeNull();
    expect(normalizeTimeZoneId(undefined)).toBeNull();
  });

  it('el alta crea la fila', () => {
    const created = projectChange(null, {
      tenantId: T,
      timeZoneId: 'America/La_Paz',
      sourceVersion: null,
      changedAtUtc: new Date('2026-11-14T09:00:00Z'),
    });

    expect(created).toEqual({
      tenantId: T,
      timeZoneId: 'America/La_Paz',
      sourceVersion: null,
      sourceUpdatedAtUtc: new Date('2026-11-14T09:00:00Z'),
    });
  });

  it('con las dos versiones manda la versión: una más vieja no pisa', () => {
    const older = projectChange(row(), {
      tenantId: T,
      timeZoneId: 'Pacific/Honolulu',
      sourceVersion: 2,
      changedAtUtc: new Date('2026-11-14T11:00:00Z'),
    });
    const newer = projectChange(row(), {
      tenantId: T,
      timeZoneId: 'Pacific/Honolulu',
      sourceVersion: 4,
      changedAtUtc: new Date('2026-11-14T09:00:00Z'),
    });

    expect(older).toBeNull();
    expect(newer).toMatchObject({ timeZoneId: 'Pacific/Honolulu', sourceVersion: 4 });
  });

  it('sin versión manda la hora del cambio, y un duplicado no escribe', () => {
    const stale = projectChange(row(), {
      tenantId: T,
      timeZoneId: 'America/Chicago',
      sourceVersion: null,
      changedAtUtc: new Date('2026-11-14T09:59:59Z'),
    });
    const duplicate = projectChange(row(), {
      tenantId: T,
      timeZoneId: 'America/New_York',
      sourceVersion: 3,
      changedAtUtc: new Date('2026-11-14T10:00:00Z'),
    });
    const later = projectChange(row(), {
      tenantId: T,
      timeZoneId: 'America/Chicago',
      sourceVersion: null,
      changedAtUtc: new Date('2026-11-14T10:00:01Z'),
    });

    expect(stale).toBeNull();
    expect(duplicate).toBeNull();
    // Sin versión entrante se conserva la última conocida.
    expect(later).toMatchObject({ timeZoneId: 'America/Chicago', sourceVersion: 3 });
  });

  it('la reconciliación corrige la zona y deja la hora de lectura', () => {
    const readAt = new Date('2026-11-14T12:00:00Z');

    expect(reconcileWithSource(null, T, 'America/Denver', readAt)).toEqual({
      tenantId: T,
      timeZoneId: 'America/Denver',
      sourceVersion: null,
      sourceUpdatedAtUtc: readAt,
    });
    expect(reconcileWithSource(row(), T, 'America/Denver', readAt)).toMatchObject({
      timeZoneId: 'America/Denver',
      sourceVersion: 3,
      sourceUpdatedAtUtc: readAt,
    });
    expect(reconcileWithSource(row(), T, 'America/New_York', readAt)).toBeNull();
  });
});
