import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';

function u(): string {
  return randomUUID();
}

function activeCall(kind: 'Audio' | 'Video' = 'Audio') {
  const caller = { userId: u(), displayName: 'A' };
  const callee = { userId: u(), displayName: 'B' };
  const initiated = Call.initiate({ tenantId: u(), kind, caller, callee });
  if (!initiated.isSuccess) throw new Error();
  const call = initiated.value;
  const accept = call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
  if (!accept.isSuccess) throw new Error();
  const active = call.markActive();
  if (!active.isSuccess) throw new Error();
  return { call, caller, callee };
}

describe('Call.upgradeToVideo (Fase 7)', () => {
  it('flips kind to Video when Audio + Active', () => {
    const { call, caller } = activeCall('Audio');
    const result = call.upgradeToVideo({ actorUserId: caller.userId });
    expect(result.isSuccess).toBe(true);
    expect(call.toSnapshot().kind).toBe('Video');
  });

  it('rejects when already Video (no-op guard)', () => {
    const { call, caller } = activeCall('Video');
    const result = call.upgradeToVideo({ actorUserId: caller.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.Upgrade.AlreadyVideo');
  });

  it('rejects if not participant', () => {
    const { call } = activeCall('Audio');
    const result = call.upgradeToVideo({ actorUserId: u() });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.NotParticipant');
  });

  it('rejects if not Active', () => {
    const caller = { userId: u(), displayName: 'A' };
    const callee = { userId: u(), displayName: 'B' };
    const initiated = Call.initiate({ tenantId: u(), kind: 'Audio', caller, callee });
    if (!initiated.isSuccess) throw new Error();
    // Ringing → upgrade should fail.
    const result = initiated.value.upgradeToVideo({ actorUserId: caller.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.Upgrade.InvalidState');
  });
});

describe('Call.startScreenShare / stopScreenShare (Fase 7)', () => {
  it('starts and stops with a duration', async () => {
    const { call, caller } = activeCall('Video');
    const startedAt = new Date('2026-07-16T18:00:00Z');
    const startResult = call.startScreenShare({ actorUserId: caller.userId, now: startedAt });
    expect(startResult.isSuccess).toBe(true);
    const sharing = call.toSnapshot().participants.find((p) => p.userId === caller.userId);
    expect(sharing?.screenSharing).toBe(true);

    const stopResult = call.stopScreenShare({
      actorUserId: caller.userId,
      now: new Date('2026-07-16T18:00:42Z'),
    });
    expect(stopResult.isSuccess).toBe(true);
    if (stopResult.isSuccess) {
      expect(stopResult.value.durationSeconds).toBe(42);
      expect(stopResult.value.startedAtUtc.toISOString()).toBe('2026-07-16T18:00:00.000Z');
    }
    const after = call.toSnapshot().participants.find((p) => p.userId === caller.userId);
    expect(after?.screenSharing).toBe(false);
  });

  it('rejects a second start while another participant already sharing', () => {
    const { call, caller, callee } = activeCall('Video');
    const first = call.startScreenShare({ actorUserId: caller.userId });
    expect(first.isSuccess).toBe(true);
    const second = call.startScreenShare({ actorUserId: callee.userId });
    expect(second.isSuccess).toBe(false);
    if (!second.isSuccess) expect(second.error.code).toBe('Call.ScreenShare.AnotherActive');
  });

  it('rejects double-start by the same participant', () => {
    const { call, caller } = activeCall('Video');
    call.startScreenShare({ actorUserId: caller.userId });
    const again = call.startScreenShare({ actorUserId: caller.userId });
    expect(again.isSuccess).toBe(false);
    if (!again.isSuccess) expect(again.error.code).toBe('Call.ScreenShare.AlreadySharing');
  });

  it('stop rejects if not currently sharing', () => {
    const { call, caller } = activeCall('Video');
    const result = call.stopScreenShare({ actorUserId: caller.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.ScreenShare.NotSharing');
  });

  it('only the sharer can stop (not the other party)', () => {
    const { call, caller, callee } = activeCall('Video');
    call.startScreenShare({ actorUserId: caller.userId });
    const result = call.stopScreenShare({ actorUserId: callee.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.ScreenShare.NotSharing');
  });

  it('rejects start if call is not Active', () => {
    const caller = { userId: u(), displayName: 'A' };
    const callee = { userId: u(), displayName: 'B' };
    const initiated = Call.initiate({ tenantId: u(), kind: 'Video', caller, callee });
    if (!initiated.isSuccess) throw new Error();
    // Ringing.
    const result = initiated.value.startScreenShare({ actorUserId: caller.userId });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.ScreenShare.InvalidState');
  });
});
