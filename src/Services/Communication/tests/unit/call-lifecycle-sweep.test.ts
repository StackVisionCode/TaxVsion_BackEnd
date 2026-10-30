import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { PresenceService } from '../../src/application/ports/presence-service.js';
import { sweepActiveCalls } from '../../src/application/use-cases/call-lifecycle-sweep.js';
import { RecordingEmitter, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

class InMemoryCalls {
  readonly calls = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.calls.set(call.toSnapshot().id, call);
  }
  async listEndingBefore(cutoff: Date): Promise<Call[]> {
    return [...this.calls.values()].filter((c) => {
      const s = c.toSnapshot();
      return (s.status === 'Accepted' || s.status === 'Active') && s.endsAtUtc !== null && s.endsAtUtc.getTime() <= cutoff.getTime();
    });
  }
  asRepository(): CallRepository {
    return this as unknown as CallRepository;
  }
}

function acceptedCall(now: Date, maxDurationMinutes: number | null): Call {
  const created = Call.initiate({
    tenantId: randomUUID(),
    kind: 'Audio',
    caller: { userId: randomUUID(), displayName: 'A' },
    callee: { userId: randomUUID(), displayName: 'B' },
    now,
  });
  if (!created.isSuccess) throw new Error();
  const call = created.value;
  const accepted = call.accept({ byUserId: call.toSnapshot().calleeUserId, calleeDisplayName: 'B', maxDurationMinutes, now });
  if (!accepted.isSuccess) throw new Error(accepted.error.message);
  return call;
}

describe('Call aggregate duration cap (F4)', () => {
  it('fixes endsAtUtc at accept from the plan cap and reports remaining seconds', () => {
    const now = new Date('2026-10-30T10:00:00Z');
    const call = acceptedCall(now, 30);
    expect(call.toSnapshot().endsAtUtc?.toISOString()).toBe('2026-10-30T10:30:00.000Z');
    expect(call.remainingSeconds(new Date('2026-10-30T10:29:00Z'))).toBe(60);
    expect(acceptedCall(now, null).remainingSeconds(now)).toBeNull();
  });

  it('warns once per threshold and ends by system with MaxDurationReached', () => {
    const now = new Date('2026-10-30T10:00:00Z');
    const call = acceptedCall(now, 5);
    expect(call.markDurationWarning({ thresholdSeconds: 300, now }).isSuccess).toBe(true);
    expect(call.markDurationWarning({ thresholdSeconds: 300, now }).isSuccess).toBe(false);
    expect(call.markDurationWarning({ thresholdSeconds: 60, now }).isSuccess).toBe(true);
    const end = new Date('2026-10-30T10:05:00Z');
    expect(call.endBySystem({ reason: 'MaxDurationReached', now: end }).isSuccess).toBe(true);
    const s = call.toSnapshot();
    expect(s.status).toBe('Ended');
    expect(s.endReason).toBe('MaxDurationReached');
    expect(s.durationSeconds).toBe(300);
    expect(s.participants.every((p) => p.leftAtUtc !== null)).toBe(true);
  });
});

describe('sweepActiveCalls (F4)', () => {
  function runtime() {
    const calls = new InMemoryCalls();
    const publisher = new RecordingPublisher();
    const emitter = new RecordingEmitter();
    const cleared: string[] = [];
    const usage: Array<{ tenantId: string; seconds: number }> = [];
    const deps = {
      calls: calls.asRepository(),
      publisher,
      emitter,
      presence: { clearBusy: async (i: { userId: string }) => { cleared.push(i.userId); } } as unknown as PresenceService,
      callUsage: { addSeconds: async (i: { tenantId: string; seconds: number }) => { usage.push(i); }, getSeconds: async () => 0 },
    };
    return { calls, publisher, emitter, cleared, usage, deps };
  }

  it('emits the 1-minute warning once and then cuts the call for both parties', async () => {
    const rt = runtime();
    const start = new Date('2026-10-30T10:00:00Z');
    const call = acceptedCall(start, 5);
    await rt.calls.save(call);

    const warned = await sweepActiveCalls({ now: new Date('2026-10-30T10:04:10Z') }, rt.deps);
    expect(warned).toEqual({ checked: 1, warned: 1, endedByTime: 0 });
    expect(rt.emitter.emitted.map((e) => e.event)).toEqual(['call.duration.warning']);
    expect(rt.publisher.types()).toEqual(['communication.call.duration_warning.v1']);

    const again = await sweepActiveCalls({ now: new Date('2026-10-30T10:04:20Z') }, rt.deps);
    expect(again.warned).toBe(0);

    const ended = await sweepActiveCalls({ now: new Date('2026-10-30T10:05:01Z') }, rt.deps);
    expect(ended.endedByTime).toBe(1);
    expect(rt.publisher.types()).toContain('communication.call.ended.v1');
    const state = rt.emitter.emitted.find((e) => e.event === 'call.state_changed')?.payload as { endReason: string; status: string };
    expect(state).toMatchObject({ status: 'Ended', endReason: 'MaxDurationReached' });
    expect(rt.cleared).toHaveLength(2);
    expect(rt.usage[0]?.seconds).toBe(301);
  });

  it('ignores calls without a cap', async () => {
    const rt = runtime();
    await rt.calls.save(acceptedCall(new Date('2026-10-30T10:00:00Z'), null));
    const report = await sweepActiveCalls({ now: new Date('2026-10-30T12:00:00Z') }, rt.deps);
    expect(report).toEqual({ checked: 0, warned: 0, endedByTime: 0 });
  });
});
