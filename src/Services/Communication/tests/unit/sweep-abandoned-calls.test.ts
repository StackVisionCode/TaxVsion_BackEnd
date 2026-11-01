import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { CallEmptinessClock, CallRoomPresence } from '../../src/application/ports/abandoned-call-ports.js';
import type { PresenceService } from '../../src/application/ports/presence-service.js';
import { sweepAbandonedCalls } from '../../src/application/use-cases/sweep-abandoned-calls.js';
import { RecordingEmitter, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

class InMemoryCalls {
  readonly calls = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.calls.set(call.toSnapshot().id, call);
  }
  async listOpenAcceptedBefore(cutoff: Date): Promise<Call[]> {
    return [...this.calls.values()].filter((c) => {
      const s = c.toSnapshot();
      return (s.status === 'Accepted' || s.status === 'Active') && s.acceptedAtUtc !== null && s.acceptedAtUtc <= cutoff;
    });
  }
  asRepository(): CallRepository {
    return this as unknown as CallRepository;
  }
}

class MemoryClock implements CallEmptinessClock {
  readonly since = new Map<string, number>();
  async emptySince(callId: string, nowMs: number): Promise<number> {
    if (!this.since.has(callId)) this.since.set(callId, nowMs);
    return this.since.get(callId)!;
  }
  async clear(callId: string): Promise<void> {
    this.since.delete(callId);
  }
}

function acceptedCall(at: Date): Call {
  const created = Call.initiate({
    tenantId: randomUUID(),
    kind: 'Audio',
    caller: { userId: randomUUID(), displayName: 'A' },
    callee: { userId: randomUUID(), displayName: 'B' },
    now: at,
  });
  if (!created.isSuccess) throw new Error();
  const call = created.value;
  const accepted = call.accept({ byUserId: call.toSnapshot().calleeUserId, calleeDisplayName: 'B', maxDurationMinutes: null, now: at });
  if (!accepted.isSuccess) throw new Error(accepted.error.message);
  return call;
}

function runtime(connected: (callId: string) => number = () => 0) {
  const calls = new InMemoryCalls();
  const clock = new MemoryClock();
  const publisher = new RecordingPublisher();
  const emitter = new RecordingEmitter();
  const cleared: string[] = [];
  const usage: number[] = [];
  const roomPresence: CallRoomPresence = { connectedCount: async (_t, callId) => connected(callId) };
  const deps = {
    calls: calls.asRepository(),
    roomPresence,
    emptinessClock: clock,
    publisher,
    emitter,
    presence: { clearBusy: async (i: { userId: string }) => { cleared.push(i.userId); } } as unknown as PresenceService,
    callUsage: { addSeconds: async (i: { seconds: number }) => { usage.push(i.seconds); }, getSeconds: async () => 0 },
  };
  return { calls, clock, publisher, emitter, cleared, usage, deps };
}

const GRACE = 120;

describe('Call.abandon', () => {
  it('cierra como Abandoned con duración hasta la última vez que hubo alguien; 0 si la media nunca arrancó', () => {
    const t0 = new Date('2026-10-31T10:00:00Z');
    const neverStarted = acceptedCall(t0);
    expect(neverStarted.abandon({ lastSeenAt: new Date('2026-10-31T12:00:00Z') }).isSuccess).toBe(true);
    expect(neverStarted.toSnapshot()).toMatchObject({ status: 'Ended', endReason: 'Abandoned', durationSeconds: 0 });

    const talked = acceptedCall(t0);
    talked.markActive(t0);
    talked.abandon({ lastSeenAt: new Date('2026-10-31T10:10:00Z'), now: new Date('2026-10-31T10:12:00Z') });
    const s = talked.toSnapshot();
    expect(s.durationSeconds).toBe(600);
    expect(s.endedAtUtc?.toISOString()).toBe('2026-10-31T10:10:00.000Z');
    expect(s.participants.every((p) => p.leftAtUtc !== null)).toBe(true);
  });

  it('no toca una llamada que ya terminó', () => {
    const call = acceptedCall(new Date('2026-10-31T10:00:00Z'));
    call.abandon({ lastSeenAt: new Date('2026-10-31T10:01:00Z') });
    expect(call.abandon({ lastSeenAt: new Date('2026-10-31T10:02:00Z') }).isSuccess).toBe(false);
  });
});

describe('sweepAbandonedCalls', () => {
  it('una llamada vieja sin nadie en la room se cierra tras la gracia (las fantasma de septiembre)', async () => {
    const rt = runtime();
    const ghost = acceptedCall(new Date('2026-09-04T15:52:40Z'));
    await rt.calls.save(ghost);
    const now = new Date('2026-10-31T23:00:00Z');

    const first = await sweepAbandonedCalls({ graceSeconds: GRACE, now }, rt.deps);
    expect(first).toEqual({ checked: 1, abandoned: 0 }); // recién se vio vacía: empieza la gracia

    const later = new Date(now.getTime() + GRACE * 1000);
    const second = await sweepAbandonedCalls({ graceSeconds: GRACE, now: later }, rt.deps);
    expect(second).toEqual({ checked: 1, abandoned: 1 });
    const s = ghost.toSnapshot();
    expect(s).toMatchObject({ status: 'Ended', endReason: 'Abandoned', durationSeconds: 0 });
    expect(rt.publisher.types()).toEqual(['communication.call.ended.v1']);
    expect(rt.emitter.emitted.filter((e) => e.target === 'user').map((e) => e.id).sort()).toEqual(
      [s.callerUserId, s.calleeUserId].sort(),
    );
    expect(rt.cleared.sort()).toEqual([s.callerUserId, s.calleeUserId].sort());
    expect(rt.usage).toEqual([]); // sin minutos que cobrar
    expect(rt.clock.since.size).toBe(0);
  });

  it('si alguien vuelve a la room durante la gracia, la cuenta se reinicia y no se cierra', async () => {
    let inRoom = 0;
    const rt = runtime(() => inRoom);
    const call = acceptedCall(new Date('2026-10-31T10:00:00Z'));
    call.markActive(new Date('2026-10-31T10:00:05Z'));
    await rt.calls.save(call);
    const t1 = new Date('2026-10-31T10:30:00Z');

    await sweepAbandonedCalls({ graceSeconds: GRACE, now: t1 }, rt.deps);
    inRoom = 1;
    await sweepAbandonedCalls({ graceSeconds: GRACE, now: new Date(t1.getTime() + 60_000) }, rt.deps);
    expect(rt.clock.since.size).toBe(0);
    inRoom = 0;
    const report = await sweepAbandonedCalls({ graceSeconds: GRACE, now: new Date(t1.getTime() + GRACE * 1000) }, rt.deps);
    expect(report.abandoned).toBe(0);
    expect(call.toSnapshot().status).toBe('Active');
  });

  it('con media en curso, cobra solo hasta que la room quedó vacía', async () => {
    const rt = runtime();
    const call = acceptedCall(new Date('2026-10-31T10:00:00Z'));
    call.markActive(new Date('2026-10-31T10:00:00Z'));
    await rt.calls.save(call);
    const emptyAt = new Date('2026-10-31T10:20:00Z');
    await sweepAbandonedCalls({ graceSeconds: GRACE, now: emptyAt }, rt.deps);
    await sweepAbandonedCalls({ graceSeconds: GRACE, now: new Date(emptyAt.getTime() + GRACE * 1000) }, rt.deps);
    expect(call.toSnapshot().durationSeconds).toBe(1200);
    expect(rt.usage).toEqual([1200]);
  });

  it('una llamada recién aceptada no se mira (la room puede estar llenándose)', async () => {
    const rt = runtime();
    const now = new Date('2026-10-31T10:01:00Z');
    await rt.calls.save(acceptedCall(new Date('2026-10-31T10:00:30Z')));
    expect(await sweepAbandonedCalls({ graceSeconds: GRACE, now }, rt.deps)).toEqual({ checked: 0, abandoned: 0 });
  });
});
