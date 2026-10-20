import { describe, expect, it, vi } from 'vitest';
import type { PresenceAudienceRepository } from '../../src/application/ports/presence-audience-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { dispatchPresenceChanged } from '../../src/infrastructure/redis/presence-changed-watcher.js';

describe('dispatchPresenceChanged', () => {
  it('emits to staff and only to assigned active portal users', async () => {
    const emitter = {
      emitToTenant: vi.fn(),
      emitToTenantStaff: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;
    const audiences = {
      findActivePortalUserIdsForPreparer: vi.fn(async () => ['portal-assigned']),
      findStaffUserIdsVisibleToPortalUser: vi.fn(async () => []),
    } satisfies PresenceAudienceRepository;

    await dispatchPresenceChanged(
      'tenant-1',
      {
        userId: 'preparer-1',
        status: 'Online',
        busyReason: null,
        changedAtUtc: '2026-10-17T15:00:00.000Z',
      },
      emitter,
      audiences,
    );

    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(1);
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
    expect(emitter.emitToUser).toHaveBeenCalledTimes(1);
    expect(emitter.emitToUser).toHaveBeenCalledWith(
      expect.objectContaining({ tenantId: 'tenant-1', userId: 'portal-assigned' }),
    );
  });

  it('does not emit to any portal user when no assignment exists', async () => {
    const emitter = {
      emitToTenant: vi.fn(),
      emitToTenantStaff: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;
    const audiences = {
      findActivePortalUserIdsForPreparer: vi.fn(async () => []),
      findStaffUserIdsVisibleToPortalUser: vi.fn(async () => []),
    } satisfies PresenceAudienceRepository;

    await dispatchPresenceChanged(
      'tenant-1',
      {
        userId: 'preparer-1',
        status: 'Offline',
        busyReason: null,
        changedAtUtc: '2026-10-17T15:00:00.000Z',
      },
      emitter,
      audiences,
    );

    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(1);
    expect(emitter.emitToUser).not.toHaveBeenCalled();
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
  });
});
