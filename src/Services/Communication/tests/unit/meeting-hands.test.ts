import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingEventLogEntry } from '../../src/domain/meetings/meeting-event-log.js';
import type { MeetingEventLogRepository } from '../../src/application/ports/meeting-event-log-repository.js';
import { lowerAllHands, lowerParticipantHand } from '../../src/application/use-cases/meeting-hand-actions.js';
import { updateRaiseHand } from '../../src/application/use-cases/update-meeting-media-status.js';

const u = (): string => randomUUID();
const T1 = new Date('2026-10-30T10:00:00.000Z');
const T2 = new Date('2026-10-30T10:00:05.000Z');

/** Reunion en vivo con host y dos asistentes admitidos. */
function liveMeeting(): { meeting: Meeting; hostUserId: string; a: string; b: string } {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'Consulta', host });
  if (!scheduled.isSuccess) throw new Error('schedule failed');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  const a = u();
  const b = u();
  for (const userId of [a, b]) {
    meeting.requestJoin({ userId, displayName: 'P', hasValidInvitation: false, passcodeMatch: null });
    meeting.admit({ hostUserId: host.userId, targetUserId: userId });
  }
  return { meeting, hostUserId: host.userId, a, b };
}

function participantOf(meeting: Meeting, userId: string) {
  const found = meeting.toSnapshot().participants.find((p) => p.userId === userId);
  if (!found) throw new Error('participant not found');
  return found;
}

function deps(meeting: Meeting): { meetings: MeetingRepository; meetingEvents: MeetingEventLogRepository; saves: number; events: MeetingEventLogEntry[] } {
  const state = { saves: 0, events: [] as MeetingEventLogEntry[] };
  const meetings = {
    findById: async () => meeting,
    save: async () => {
      state.saves += 1;
    },
  } as unknown as MeetingRepository;
  const meetingEvents = {
    append: async (entry: MeetingEventLogEntry) => {
      state.events.push(entry);
    },
  } as unknown as MeetingEventLogRepository;
  return {
    meetings,
    meetingEvents,
    get saves() {
      return state.saves;
    },
    get events() {
      return state.events;
    },
  };
}

describe('manos con timestamp (F5, D-49)', () => {
  it('levantar fija handRaisedAtUtc una sola vez; bajar la limpia', () => {
    const { meeting, a } = liveMeeting();
    expect(meeting.raiseHand({ byUserId: a, raised: true, now: T1 }).isSuccess).toBe(true);
    expect(participantOf(meeting, a)).toMatchObject({ handRaised: true, handRaisedAtUtc: T1 });
    // Repetir "raised" no mueve el turno en la cola.
    meeting.raiseHand({ byUserId: a, raised: true, now: T2 });
    expect(participantOf(meeting, a).handRaisedAtUtc).toEqual(T1);
    meeting.raiseHand({ byUserId: a, raised: false, now: T2 });
    expect(participantOf(meeting, a)).toMatchObject({ handRaised: false, handRaisedAtUtc: null });
  });

  it('lowerHand exige host o cohost y es idempotente', () => {
    const { meeting, hostUserId, a, b } = liveMeeting();
    meeting.raiseHand({ byUserId: b, raised: true, now: T1 });
    const denied = meeting.lowerHand({ hostUserId: a, targetUserId: b });
    expect(denied).toMatchObject({ isSuccess: false, error: { code: 'Meeting.HostOnly' } });
    expect(meeting.lowerHand({ hostUserId, targetUserId: b, now: T2 })).toMatchObject({ isSuccess: true, value: { changed: true } });
    expect(participantOf(meeting, b).handRaised).toBe(false);
    expect(meeting.lowerHand({ hostUserId, targetUserId: b })).toMatchObject({ isSuccess: true, value: { changed: false } });
  });

  it('lowerAllHands baja solo las levantadas y devuelve sus userIds', () => {
    const { meeting, hostUserId, a, b } = liveMeeting();
    meeting.raiseHand({ byUserId: a, raised: true, now: T1 });
    const result = meeting.lowerAllHands({ hostUserId, now: T2 });
    expect(result).toMatchObject({ isSuccess: true, value: { lowered: [a] } });
    expect(participantOf(meeting, a).handRaised).toBe(false);
    expect(participantOf(meeting, b).handRaised).toBe(false);
    expect(meeting.lowerAllHands({ hostUserId: b })).toMatchObject({ isSuccess: false, error: { code: 'Meeting.HostOnly' } });
  });

  it('el re-join completo baja la mano y lo dice; la sala de espera no', () => {
    const { meeting, a } = liveMeeting();
    meeting.raiseHand({ byUserId: a, raised: true, now: T1 });
    const again = meeting.requestJoin({ userId: a, displayName: 'P', hasValidInvitation: false, passcodeMatch: null });
    expect(again).toMatchObject({ isSuccess: true, value: { rejoined: true, handLoweredOnRejoin: true } });
    expect(participantOf(meeting, a)).toMatchObject({ handRaised: false, handRaisedAtUtc: null });
    const fresh = meeting.requestJoin({ userId: u(), displayName: 'Nuevo', hasValidInvitation: false, passcodeMatch: null });
    expect(fresh).toMatchObject({ isSuccess: true, value: { requiresAdmission: true, handLoweredOnRejoin: false } });
  });

  it('salir de la reunion limpia la mano', () => {
    const { meeting, a } = liveMeeting();
    meeting.raiseHand({ byUserId: a, raised: true, now: T1 });
    meeting.leave({ userId: a, now: T2 });
    expect(participantOf(meeting, a)).toMatchObject({ status: 'Left', handRaised: false, handRaisedAtUtc: null });
  });
});

describe('casos de uso de manos (F5)', () => {
  it('updateRaiseHand devuelve el DTO con handRaisedAtUtc en ISO y registra hand.raised', async () => {
    const { meeting, a } = liveMeeting();
    const d = deps(meeting);
    const result = await updateRaiseHand({ tenantId: meeting.tenantId, meetingId: meeting.id, actorUserId: a, raised: true }, d);
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.handRaised).toBe(true);
      expect(typeof result.value.handRaisedAtUtc).toBe('string');
    }
    expect(d.events.map((e) => e.type)).toEqual(['hand.raised']);
  });

  it('lowerParticipantHand guarda y registra hand.lowered_by_host solo si cambio algo', async () => {
    const { meeting, hostUserId, a } = liveMeeting();
    meeting.raiseHand({ byUserId: a, raised: true, now: T1 });
    const d = deps(meeting);
    const first = await lowerParticipantHand({ tenantId: meeting.tenantId, meetingId: meeting.id, hostUserId, targetUserId: a }, d);
    expect(first).toMatchObject({ isSuccess: true, value: { changed: true, participant: { userId: a, handRaised: false, handRaisedAtUtc: null } } });
    expect(d.saves).toBe(1);
    expect(d.events[0]).toMatchObject({ type: 'hand.lowered_by_host', actorUserId: hostUserId, targetUserId: a });
    const second = await lowerParticipantHand({ tenantId: meeting.tenantId, meetingId: meeting.id, hostUserId, targetUserId: a }, d);
    expect(second).toMatchObject({ isSuccess: true, value: { changed: false } });
    expect(d.saves).toBe(1);
    expect(d.events).toHaveLength(1);
  });

  it('lowerAllHands devuelve los participantes bajados y registra hand.lowered_all con el conteo', async () => {
    const { meeting, hostUserId, a, b } = liveMeeting();
    meeting.raiseHand({ byUserId: a, raised: true, now: T1 });
    meeting.raiseHand({ byUserId: b, raised: true, now: T2 });
    const d = deps(meeting);
    const result = await lowerAllHands({ tenantId: meeting.tenantId, meetingId: meeting.id, hostUserId }, d);
    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) {
      expect(result.value.participants.map((p) => p.userId).sort()).toEqual([a, b].sort());
      expect(result.value.participants.every((p) => !p.handRaised && p.handRaisedAtUtc === null)).toBe(true);
    }
    expect(d.events[0]).toMatchObject({ type: 'hand.lowered_all', actorUserId: hostUserId, payload: { count: 2 } });
    const empty = await lowerAllHands({ tenantId: meeting.tenantId, meetingId: meeting.id, hostUserId }, d);
    expect(empty).toMatchObject({ isSuccess: true, value: { participants: [] } });
    expect(d.saves).toBe(1);
  });
});
