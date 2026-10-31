import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { expireStaleScheduledMeetings, sweepLiveMeetings } from '../../src/application/use-cases/meeting-lifecycle-sweep.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();
const MIN = 60_000;
const T0 = new Date('2026-10-29T10:00:00Z');

function liveMeeting(opts: { maxDurationMinutes: number; startedAt?: Date }) {
  const host = { userId: u(), displayName: 'Host' };
  const r = Meeting.schedule({ tenantId: u(), title: 'Kickoff', host, requireWaitingRoom: false });
  if (!r.isSuccess) throw new Error();
  const meeting = r.value;
  meeting.start({ hostUserId: host.userId, maxDurationMinutes: opts.maxDurationMinutes, now: opts.startedAt ?? T0 });
  return { meeting, host };
}

describe('sweepLiveMeetings', () => {
  it('warns once per threshold (10/5/1) and never repeats within the same threshold', async () => {
    const rt = buildMeetingRuntime();
    const { meeting } = liveMeeting({ maxDurationMinutes: 60 });
    await rt.repository.save(meeting);

    const at = (minutesBeforeEnd: number) => new Date(T0.getTime() + (60 - minutesBeforeEnd) * MIN);
    expect((await sweepLiveMeetings({ now: at(15) }, rt.deps)).warned).toBe(0);
    expect((await sweepLiveMeetings({ now: at(9) }, rt.deps)).warned).toBe(1);
    expect((await sweepLiveMeetings({ now: at(8) }, rt.deps)).warned).toBe(0);
    expect((await sweepLiveMeetings({ now: at(4) }, rt.deps)).warned).toBe(1);
    expect((await sweepLiveMeetings({ now: at(0.5) }, rt.deps)).warned).toBe(1);

    const warnings = rt.emitter.emitted.filter((e) => e.event === 'meeting.duration.warning');
    expect(warnings.map((w) => (w.payload as { remainingSeconds: number }).remainingSeconds)).toEqual([540, 240, 30]);
    expect(rt.publisher.types().filter((t) => t === 'communication.meeting.duration_warning.v1')).toHaveLength(3);
    expect(rt.meetingEvents.types(meeting.id).filter((t) => t === 'meeting.duration_warning')).toHaveLength(3);
  });

  it('ends the meeting by MaxDurationReached at endsAtUtc with the orderly close', async () => {
    const rt = buildMeetingRuntime();
    const { meeting, host } = liveMeeting({ maxDurationMinutes: 30 });
    await rt.repository.save(meeting);

    const report = await sweepLiveMeetings({ now: new Date(T0.getTime() + 30 * MIN) }, rt.deps);
    expect(report.endedByTime).toBe(1);
    const after = rt.repository.meetings.get(meeting.id)!.toSnapshot();
    expect(after.status).toBe('Ended');
    expect(after.endReason).toBe('MaxDurationReached');
    expect(rt.sfu.closedMeetings).toEqual([meeting.id]);
    expect(rt.presence.cleared).toContain(host.userId);
    const ended = rt.publisher.events.find((e) => e.eventType === 'communication.meeting.ended.v1') as { reason?: string } | undefined;
    expect(ended?.reason).toBe('MaxDurationReached');
    const state = rt.emitter.emitted.find((e) => e.target === 'meeting' && e.event === 'meeting.state.changed');
    expect((state?.payload as { status: string; endReason: string }).endReason).toBe('MaxDurationReached');
    expect(rt.emitter.emitted.some((e) => e.target === 'user' && e.id === host.userId && e.event === 'meeting.ended')).toBe(true);
  });

  it('ends an empty meeting after the office emptyEndMinutes, not before', async () => {
    const rt = buildMeetingRuntime({ emptyMeetingEndMinutes: 10 });
    const { meeting, host } = liveMeeting({ maxDurationMinutes: 60 });
    meeting.leave({ userId: host.userId, now: new Date(T0.getTime() + 5 * MIN) });
    await rt.repository.save(meeting);

    expect((await sweepLiveMeetings({ now: new Date(T0.getTime() + 14 * MIN) }, rt.deps)).endedEmpty).toBe(0);
    expect((await sweepLiveMeetings({ now: new Date(T0.getTime() + 15 * MIN) }, rt.deps)).endedEmpty).toBe(1);
    expect(rt.repository.meetings.get(meeting.id)!.toSnapshot().endReason).toBe('NoParticipants');
  });
});

describe('expireStaleScheduledMeetings', () => {
  it('expires only the scheduled meetings older than the office expiry hours and reports dry runs', async () => {
    const rt = buildMeetingRuntime({ scheduledMeetingExpiryHours: 24 });
    const host = { userId: u(), displayName: 'Host' };
    const old = Meeting.schedule({ tenantId: u(), title: 'old', host, scheduledForUtc: new Date(T0.getTime() - 30 * 60 * MIN) });
    const recent = Meeting.schedule({ tenantId: u(), title: 'recent', host, scheduledForUtc: new Date(T0.getTime() - 2 * 60 * MIN) });
    if (!old.isSuccess || !recent.isSuccess) throw new Error();
    await rt.repository.save(old.value);
    await rt.repository.save(recent.value);

    const dry = await expireStaleScheduledMeetings({ now: T0, dryRun: true }, rt.deps);
    expect(dry.expired).toBe(1);
    expect(old.value.status).toBe('Scheduled');

    const real = await expireStaleScheduledMeetings({ now: T0 }, rt.deps);
    expect(real.expired).toBe(1);
    expect(old.value.status).toBe('Expired');
    expect(recent.value.status).toBe('Scheduled');
    expect(rt.publisher.types()).toEqual(['communication.meeting.expired.v1']);
    expect(rt.meetingEvents.types(old.value.id)).toEqual(['meeting.expired']);
    const listNotice = rt.emitter.emitted.find((e) => e.target === 'user' && e.id === host.userId);
    expect(listNotice?.payload).toEqual({ meetingId: old.value.id, reason: 'Expired' });
  });
});
