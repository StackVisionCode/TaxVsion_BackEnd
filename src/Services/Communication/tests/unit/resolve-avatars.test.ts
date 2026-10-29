import { describe, expect, it, vi } from 'vitest';
import {
  DirectoryAvatarResolver,
  pickAvatarFileId,
  resolveAvatars,
} from '../../src/application/use-cases/resolve-avatars.js';
import type {
  AvatarLookupRepository,
  AvatarLookupRow,
} from '../../src/application/ports/avatar-lookup-repository.js';

/**
 * F5 avatares — regla unica de foto: staff desde UserDirectoryEntry, cliente del portal desde la ficha
 * del cliente (CustomerPortalAccount → CustomerDirectoryEntry), y una sola consulta por lote.
 */
const STAFF = 'aaaaaaaa-0000-0000-0000-000000000001';
const PORTAL = 'aaaaaaaa-0000-0000-0000-000000000002';
const GUEST = 'aaaaaaaa-0000-0000-0000-000000000003';

function row(overrides: Partial<AvatarLookupRow> & { userId: string }): AvatarLookupRow {
  return {
    actorType: null,
    userAvatarFileId: null,
    customerAvatarFileId: null,
    hasActivePortalAccount: false,
    ...overrides,
  };
}

function lookupWith(
  rows: AvatarLookupRow[],
): AvatarLookupRepository & { findByUserIds: ReturnType<typeof vi.fn> } {
  return {
    findByUserIds: vi.fn(async (ids: readonly string[]) => rows.filter((r) => ids.includes(r.userId))),
  };
}

describe('pickAvatarFileId', () => {
  it('staff → foto de Auth', () => {
    expect(
      pickAvatarFileId(row({ userId: STAFF, actorType: 'TenantEmployee', userAvatarFileId: 'f-staff' })),
    ).toBe('f-staff');
  });

  it('portal con cuenta activa → foto de la ficha del cliente, nunca la de Auth', () => {
    expect(
      pickAvatarFileId(
        row({
          userId: PORTAL,
          actorType: 'CustomerPortal',
          userAvatarFileId: 'f-ignored',
          customerAvatarFileId: 'f-customer',
          hasActivePortalAccount: true,
        }),
      ),
    ).toBe('f-customer');
  });

  it('portal sin cuenta activa → null (no cae a Auth)', () => {
    expect(
      pickAvatarFileId(row({ userId: PORTAL, actorType: 'CustomerPortal', userAvatarFileId: 'f-ignored' })),
    ).toBeNull();
  });
});

describe('DirectoryAvatarResolver', () => {
  it('resuelve staff, portal y desconocidos en UNA consulta, deduplicando y sin importar mayusculas', async () => {
    const lookup = lookupWith([
      row({ userId: STAFF, actorType: 'TenantAdmin', userAvatarFileId: 'f-staff' }),
      row({
        userId: PORTAL,
        actorType: 'CustomerPortal',
        customerAvatarFileId: 'f-customer',
        hasActivePortalAccount: true,
      }),
    ]);
    const resolver = new DirectoryAvatarResolver(lookup);

    const avatars = await resolver.resolve([STAFF, STAFF.toUpperCase(), PORTAL, GUEST, '  ']);

    expect(lookup.findByUserIds).toHaveBeenCalledTimes(1);
    expect(lookup.findByUserIds).toHaveBeenCalledWith([STAFF, PORTAL, GUEST]);
    expect(avatars.of(STAFF)).toBe('f-staff');
    expect(avatars.of(STAFF.toUpperCase())).toBe('f-staff');
    expect(avatars.of(PORTAL)).toBe('f-customer');
    expect(avatars.of(GUEST)).toBeNull();
  });

  it('no consulta si no hay ids', async () => {
    const lookup = lookupWith([]);
    const avatars = await new DirectoryAvatarResolver(lookup).resolve([]);
    expect(lookup.findByUserIds).not.toHaveBeenCalled();
    expect(avatars.of(STAFF)).toBeNull();
  });

  it('parte en lotes de 500 para no pasar el limite de parametros de SQL Server', async () => {
    const lookup = lookupWith([]);
    const ids = Array.from({ length: 501 }, (_, i) => `id-${i}`);
    await new DirectoryAvatarResolver(lookup).resolve(ids);
    expect(lookup.findByUserIds).toHaveBeenCalledTimes(2);
    expect(lookup.findByUserIds.mock.calls[0]![0]).toHaveLength(500);
    expect(lookup.findByUserIds.mock.calls[1]![0]).toHaveLength(1);
  });
});

describe('resolveAvatars (fail-soft)', () => {
  it('sin resolver devuelve undefined (los DTOs omiten el campo)', async () => {
    expect(await resolveAvatars(undefined, [STAFF])).toBeUndefined();
  });

  it('si la consulta falla devuelve undefined en vez de lanzar', async () => {
    const resolver = { resolve: vi.fn().mockRejectedValue(new Error('db down')) };
    expect(await resolveAvatars(resolver, [STAFF])).toBeUndefined();
  });
});
