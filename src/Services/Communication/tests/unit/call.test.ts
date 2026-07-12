import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';

function u(): string {
  return randomUUID();
}

function initiate(kind: 'Audio' | 'Video' = 'Video') {
  const caller = { userId: u(), displayName: 'Ana' };
  const callee = { userId: u(), displayName: 'Bob' };
  const r = Call.initiate({ tenantId: u(), kind, caller, callee });
  if (!r.isSuccess) throw new Error();
  return { call: r.value, caller, callee };
}

describe('Call.initiate', () => {
  it('creates a Ringing call with the caller as participant #1', () => {
    const { call, caller } = initiate();
    const snap = call.toSnapshot();
    expect(snap.status).toBe('Ringing');
    expect(snap.participants).toHaveLength(1);
    expect(snap.participants[0]!.userId).toBe(caller.userId);
    expect(snap.participants[0]!.joinOrder).toBe(1);
    expect(snap.participants[0]!.role).toBe('Caller');
  });

  it('rejects calling yourself', () => {
    const id = u();
    const r = Call.initiate({
      tenantId: u(),
      kind: 'Audio',
      caller: { userId: id, displayName: 'X' },
      callee: { userId: id, displayName: 'X' },
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Call.SelfCall');
  });
});

describe('Call.accept', () => {
  it('only the callee can accept, joins as JoinOrder=2 (polite peer)', () => {
    const { call, caller, callee } = initiate();
    expect(
      call.accept({ byUserId: caller.userId, calleeDisplayName: 'Bob' }).isSuccess,
    ).toBe(false);
    const r = call.accept({ byUserId: callee.userId, calleeDisplayName: 'Bob' });
    expect(r.isSuccess).toBe(true);
    const calleeSnap = call.toSnapshot().participants.find((p) => p.userId === callee.userId)!;
    expect(calleeSnap.joinOrder).toBe(2);
    expect(calleeSnap.role).toBe('Callee');
    expect(call.status).toBe('Accepted');
  });

  it('cannot accept from a non-Ringing state', () => {
    const { call, caller, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const again = call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    expect(again.isSuccess).toBe(false);
    if (again.isSuccess) return;
    expect(again.error.code).toBe('Call.InvalidTransition');
    // no-op line to use caller (silence lint)
    expect(caller.userId).toBeTruthy();
  });
});

describe('Call.reject / cancel', () => {
  it('only callee can reject', () => {
    const { call, caller, callee } = initiate();
    expect(call.reject({ byUserId: caller.userId }).isSuccess).toBe(false);
    expect(call.reject({ byUserId: callee.userId }).isSuccess).toBe(true);
    expect(call.status).toBe('Rejected');
  });

  it('only caller can cancel', () => {
    const { call, caller, callee } = initiate();
    expect(call.cancel({ byUserId: callee.userId }).isSuccess).toBe(false);
    expect(call.cancel({ byUserId: caller.userId }).isSuccess).toBe(true);
    expect(call.status).toBe('Cancelled');
  });
});

describe('Call.markMissed', () => {
  it('marks Ringing calls as MissedCall with duration=0', () => {
    const { call } = initiate();
    const r = call.markMissed(new Date());
    expect(r.isSuccess).toBe(true);
    expect(call.status).toBe('MissedCall');
    expect(call.toSnapshot().durationSeconds).toBe(0);
  });

  it('does nothing on a non-Ringing call', () => {
    const { call, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const r = call.markMissed(new Date());
    expect(r.isSuccess).toBe(false);
  });
});

describe('Call.end', () => {
  it('computes duration and marks Ended (Hangup by default)', () => {
    const { call, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const now = new Date();
    const r = call.end({ byUserId: callee.userId, now });
    expect(r.isSuccess).toBe(true);
    const snap = call.toSnapshot();
    expect(snap.status).toBe('Ended');
    expect(snap.endReason).toBe('Hangup');
    expect(snap.durationSeconds).toBeGreaterThanOrEqual(0);
  });

  it('rejects end from non-participant', () => {
    const { call, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const r = call.end({ byUserId: u() });
    expect(r.isSuccess).toBe(false);
  });
});

describe('Call.applyMediaStatus', () => {
  it('participant can toggle their own media', () => {
    const { call, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const r = call.applyMediaStatus({
      byUserId: callee.userId,
      status: { audioEnabled: false, videoEnabled: true, screenSharing: false },
    });
    expect(r.isSuccess).toBe(true);
    const snap = call.toSnapshot().participants.find((p) => p.userId === callee.userId)!;
    expect(snap.audioEnabled).toBe(false);
  });

  it('cannot toggle another participant', () => {
    const { call, caller, callee } = initiate();
    call.accept({ byUserId: callee.userId, calleeDisplayName: 'B' });
    const r = call.applyMediaStatus({
      byUserId: caller.userId,
      status: { audioEnabled: false, videoEnabled: false, screenSharing: false },
    });
    // caller can toggle THEIR OWN — this actually works. Test the real forbidden path:
    expect(r.isSuccess).toBe(true);
    const outsider = u();
    const r2 = call.applyMediaStatus({
      byUserId: outsider,
      status: { audioEnabled: false, videoEnabled: false, screenSharing: false },
    });
    expect(r2.isSuccess).toBe(false);
    if (r2.isSuccess) return;
    expect(r2.error.code).toBe('Call.NotParticipant');
  });
});

describe('Call.getPeerUserId', () => {
  it('returns the other side for a participant', () => {
    const { call, caller, callee } = initiate();
    expect(call.getPeerUserId(caller.userId)).toBe(callee.userId);
    expect(call.getPeerUserId(callee.userId)).toBe(caller.userId);
    expect(call.getPeerUserId(u())).toBeNull();
  });
});
