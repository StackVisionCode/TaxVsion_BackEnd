import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { forceLeaveMeeting } from '../../src/application/use-cases/force-leave-meeting.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();

function liveWith(actorTypes: string[]) {
  const host = { userId: u(), displayName: 'Host' };
  const r = Meeting.schedule({ tenantId: u(), title: 'Kickoff', host, requireWaitingRoom: false });
  if (!r.isSuccess) throw new Error();
  const meeting = r.value;
  meeting.start({ hostUserId: host.userId, maxDurationMinutes: 60 });
  const others = actorTypes.map((actorType) => {
    const userId = u();
    meeting.requestJoin({ userId, displayName: actorType, actorType, hasValidInvitation: false, passcodeMatch: null });
    return userId;
  });
  return { meeting, host, others };
}

describe('forceLeaveMeeting', () => {
  it('removes an attendee, tells the room and the socket, frees SFU and busy, and leaves a trace', async () => {
    const rt = buildMeetingRuntime();
    const { meeting, others } = liveWith(['TenantEmployee']);
    await rt.repository.save(meeting);
    const [attendee] = others;

    const r = await forceLeaveMeeting(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: attendee!, reason: 'Revoked', correlationId: 'c', socketId: 'sock-1' },
      rt.deps,
    );
    expect(r.isSuccess && r.value.meetingEnded).toBe(false);
    expect(rt.repository.meetings.get(meeting.id)!.isJoinedParticipant(attendee!)).toBe(false);
    expect(rt.emitter.events('meeting')).toEqual(['meeting.participant.changed', 'meeting.state.changed']);
    expect(rt.emitter.emitted.find((e) => e.target === 'socket')?.payload).toEqual({ meetingId: meeting.id, reason: 'Revoked' });
    expect(rt.sfu.closedParticipants).toEqual([attendee]);
    expect(rt.presence.cleared).toEqual([attendee]);
    expect(rt.publisher.types()).toEqual([
      'communication.meeting.participant_left.v1',
      'communication.meeting.participant_forced_leave.v1',
    ]);
    expect(rt.meetingEvents.types(meeting.id)).toEqual(['participant.left', 'participant.forced_leave']);
  });

  it('when the host is forced out, the oldest staff member succeeds (roles, host.changed, state, event)', async () => {
    const rt = buildMeetingRuntime();
    const { meeting, host, others } = liveWith(['CustomerPortal', 'TenantEmployee']);
    await rt.repository.save(meeting);
    const staff = others[1]!;

    const r = await forceLeaveMeeting(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: host.userId, reason: 'Grace', correlationId: 'c' },
      rt.deps,
    );
    expect(r.isSuccess && r.value.meetingEnded).toBe(false);
    expect(rt.repository.meetings.get(meeting.id)!.hostUserId).toBe(staff);
    expect(rt.emitter.events('meeting')).toEqual([
      'meeting.participant.changed',
      'meeting.participant.changed',
      'meeting.participant.changed',
      'meeting.host.changed',
      'meeting.state.changed',
    ]);
    const hostChanged = rt.emitter.emitted.find((e) => e.event === 'meeting.host.changed')?.payload as { newHostUserId: string; reason: string };
    expect(hostChanged).toMatchObject({ newHostUserId: staff, reason: 'Succession' });
    const transferred = rt.publisher.events.find((e) => e.eventType === 'communication.meeting.host_transferred.v1') as { reason?: string };
    expect(transferred.reason).toBe('Succession');
    expect(rt.meetingEvents.types(meeting.id)).toContain('host.succeeded');
  });

  it('when the host is forced out and only customers remain, the meeting ends by NoParticipants', async () => {
    const rt = buildMeetingRuntime();
    const { meeting, host } = liveWith(['CustomerPortal']);
    await rt.repository.save(meeting);

    const r = await forceLeaveMeeting(
      { tenantId: meeting.tenantId, meetingId: meeting.id, userId: host.userId, reason: 'Expired', correlationId: 'c' },
      rt.deps,
    );
    expect(r.isSuccess && r.value.meetingEnded).toBe(true);
    const snap = rt.repository.meetings.get(meeting.id)!.toSnapshot();
    expect(snap.status).toBe('Ended');
    expect(snap.endReason).toBe('NoParticipants');
    expect(rt.sfu.closedMeetings).toEqual([meeting.id]);
    const ended = rt.publisher.events.find((e) => e.eventType === 'communication.meeting.ended.v1') as { reason?: string };
    expect(ended.reason).toBe('NoParticipants');
  });
});
