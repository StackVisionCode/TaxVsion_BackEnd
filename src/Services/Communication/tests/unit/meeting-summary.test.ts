import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting, type MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import type { MeetingParticipantSnapshot } from '../../src/domain/meetings/meeting-participant.js';
import { MeetingInvitation, type MeetingInvitationSnapshot } from '../../src/domain/meetings/meeting-invitation.js';
import { MeetingEventType, type MeetingEventLogEntry } from '../../src/domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingEventLogRepository } from '../../src/application/ports/meeting-event-log-repository.js';
import type { UserDirectoryEntrySnapshot } from '../../src/application/ports/user-directory-repository.js';
import { getMeetingSummary, summarizeAttendance } from '../../src/application/use-cases/get-meeting-summary.js';
import { listMeetingInvitations } from '../../src/application/use-cases/list-meeting-invitations.js';
import { listMeetingEvents } from '../../src/application/use-cases/meeting-events-query.js';
import { InMemoryMeetingEventLog } from '../helpers/fake-meeting-runtime.js';

const TENANT = 'tenant-1';
const MEETING = 'meeting-1';
const HOST = 'host-1';
const CLIENT = 'client-1';
const EMPLOYEE = 'employee-1';
const T0 = new Date('2026-10-30T15:00:00Z');
const at = (minutes: number): Date => new Date(T0.getTime() + minutes * 60_000);

function participant(userId: string, overrides: Partial<MeetingParticipantSnapshot> = {}): MeetingParticipantSnapshot {
  return {
    id: randomUUID(),
    meetingId: MEETING,
    tenantId: TENANT,
    userId,
    displayName: `Name ${userId}`,
    actorType: 'TenantEmployee',
    customerId: null,
    role: 'Attendee',
    status: 'Left',
    joinOrder: 1,
    requestedAtUtc: T0,
    admittedAtUtc: T0,
    joinedAtUtc: T0,
    leftAtUtc: at(30),
    audioEnabled: true,
    videoEnabled: true,
    screenSharing: false,
    handRaised: false,
    handRaisedAtUtc: null,
    connectionQuality: 'Unknown',
    ...overrides,
  };
}

function meeting(participants: MeetingParticipantSnapshot[]): Meeting {
  const snapshot: MeetingSnapshot = {
    id: MEETING,
    tenantId: TENANT,
    title: 'Quarterly review',
    description: null,
    status: 'Ended',
    shortCode: 'ABCDEFGH',
    passcodeHash: null,
    requireWaitingRoom: false,
    isLocked: false,
    maxParticipants: 10,
    strategy: 'Sfu',
    recordingRequested: false,
    recordingFileId: null,
    transcriptFileId: null,
    transcriptLanguage: null,
    hostUserId: HOST,
    scheduledForUtc: null,
    scheduledDurationMinutes: null,
    maxDurationMinutes: 60,
    endsAtUtc: null,
    endReason: 'Host',
    lastDurationWarningSeconds: null,
    reminderSentAtUtc: null,
    continuationOfMeetingId: null,
    startedAtUtc: T0,
    endedAtUtc: at(30),
    durationSeconds: 1800,
    createdByUserId: HOST,
    createdAtUtc: T0,
    updatedAtUtc: at(30),
    participants,
  };
  return Meeting.rehydrate(snapshot);
}

let seq = 0;
function event(type: string, minute: number, fields: Partial<MeetingEventLogEntry> = {}): MeetingEventLogEntry {
  seq += 1;
  return {
    id: randomUUID(),
    tenantId: TENANT,
    scope: 'Meeting',
    scopeId: MEETING,
    seq,
    at: at(minute),
    type: type as MeetingEventLogEntry['type'],
    actorUserId: null,
    targetUserId: null,
    payload: null,
    correlationId: null,
    ...fields,
  };
}

function invitation(overrides: Partial<MeetingInvitationSnapshot>): MeetingInvitation {
  return MeetingInvitation.rehydrate({
    id: randomUUID(),
    meetingId: MEETING,
    tenantId: TENANT,
    inviteeKind: 'Employee',
    inviteeEmail: null,
    inviteeUserId: null,
    inviteeName: null,
    inviteeExternalPhone: null,
    tokenHash: 'hash',
    expiresAtUtc: at(120),
    usedAtUtc: null,
    revokedAtUtc: null,
    firstUsedAtUtc: null,
    lastUsedAtUtc: null,
    useCount: 0,
    closedAtUtc: at(30),
    createdAtUtc: T0,
    ...overrides,
  });
}

function deps(m: Meeting, events: MeetingEventLogEntry[], invitations: MeetingInvitation[] = []) {
  const directory: UserDirectoryEntrySnapshot[] = [
    { userId: 'absent-1', tenantId: TENANT, displayName: 'Ana Absent', email: 'ana@x.test', isActive: true, actorType: 'TenantEmployee', avatarFileId: null, updatedAtUtc: T0 },
  ];
  return {
    meetings: {
      findById: async () => m,
      listInvitationsByMeeting: async () => invitations,
    } as unknown as MeetingRepository,
    meetingEvents: {
      list: async (input: { afterSeq?: number; take: number }) =>
        events.filter((e) => e.seq > (input.afterSeq ?? 0)).slice(0, input.take),
    } as unknown as MeetingEventLogRepository,
    recordingSessions: { findByScope: async () => null },
    userDirectory: { findByUserIds: async (ids: readonly string[]) => directory.filter((d) => ids.includes(d.userId)) },
  };
}

describe('summarizeAttendance', () => {
  it('suma las sesiones de quien sale y vuelve, cierra lo abierto al fin y cuenta pico, voz, pantalla y reacciones', () => {
    seq = 0;
    const events = [
      event(MeetingEventType.ParticipantJoined, 0, { actorUserId: HOST }),
      event(MeetingEventType.ParticipantJoined, 2, { actorUserId: CLIENT }),
      event(MeetingEventType.SpeakerStarted, 3, { actorUserId: CLIENT }),
      event(MeetingEventType.SpeakerStopped, 5, { actorUserId: CLIENT }),
      event(MeetingEventType.ScreenStarted, 6, { actorUserId: HOST }),
      event(MeetingEventType.HandRaised, 7, { actorUserId: CLIENT }),
      event(MeetingEventType.ParticipantLeft, 10, { actorUserId: CLIENT }),
      event(MeetingEventType.ParticipantRejoined, 15, { actorUserId: CLIENT }),
      event(MeetingEventType.ParticipantAdmitted, 16, { actorUserId: HOST, targetUserId: EMPLOYEE }),
      event(MeetingEventType.ParticipantRemoved, 20, { actorUserId: HOST, targetUserId: EMPLOYEE }),
      event(MeetingEventType.ReactionSummary, 21, { payload: { minuteUtc: 'x', counts: { '👍': 3, '🎉': 2 } } }),
    ];
    const summary = summarizeAttendance([participant(HOST), participant(CLIENT), participant(EMPLOYEE)], events, at(30));

    expect(summary.byUser.get(HOST)).toMatchObject({ attendedMs: 30 * 60_000, sessions: 1, screenShares: 1 });
    // 2→10 y 15→30 (cerrada al fin de la reunión).
    expect(summary.byUser.get(CLIENT)).toMatchObject({ attendedMs: 23 * 60_000, sessions: 2, speakingMs: 2 * 60_000, handsRaised: 1 });
    expect(summary.byUser.get(EMPLOYEE)).toMatchObject({ attendedMs: 4 * 60_000, sessions: 1, lastLeftAt: at(20) });
    expect(summary.peakConcurrent).toBe(3);
    expect(summary.reactions).toBe(5);
  });

  it('sin eventos usa la fila del participante y no cuenta a quien nunca entró', () => {
    const summary = summarizeAttendance(
      [participant(HOST, { joinedAtUtc: at(1), leftAtUtc: at(11) }), participant('waiting-1', { joinedAtUtc: null, status: 'Waiting' })],
      [],
      at(30),
    );
    expect(summary.byUser.get(HOST)).toMatchObject({ attendedMs: 10 * 60_000, sessions: 1 });
    expect(summary.byUser.has('waiting-1')).toBe(false);
  });
});

describe('getMeetingSummary', () => {
  const m = () =>
    meeting([
      participant(HOST, { role: 'Host' }),
      participant(CLIENT, { actorType: 'CustomerPortal', joinedAtUtc: at(2) }),
    ]);
  const events = () => {
    seq = 0;
    return [
      event(MeetingEventType.ParticipantJoined, 0, { actorUserId: HOST }),
      event(MeetingEventType.ParticipantJoined, 2, { actorUserId: CLIENT }),
    ];
  };
  const invitations = () => [
    invitation({ inviteeUserId: CLIENT, inviteeKind: 'Customer' }),
    invitation({ inviteeUserId: 'absent-1' }),
    invitation({ inviteeKind: 'External', inviteeEmail: 'guest@x.test' }),
    invitation({ inviteeKind: 'External', inviteeEmail: 'came@x.test', usedAtUtc: at(3) }),
  ];

  it('el host ve la asistencia, el nombre del host y quiénes no entraron (con nombre real, no el id)', async () => {
    const result = await getMeetingSummary(
      { tenantId: TENANT, meetingId: MEETING, userId: HOST, isPortal: false, recordingVisible: true },
      deps(m(), events(), invitations()),
    );
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.meeting).toMatchObject({ title: 'Quarterly review', hostName: `Name ${HOST}`, durationSeconds: 1800 });
    expect(result.value.participants.map((p) => [p.userId, p.attendedSeconds])).toEqual([
      [HOST, 1800],
      [CLIENT, 1680],
    ]);
    expect(result.value.invitedNotJoined).toEqual([
      { kind: 'Employee', displayName: 'Ana Absent' },
      { kind: 'External', displayName: 'guest@x.test' },
    ]);
    expect(result.value.totals).toMatchObject({ attendees: 2, peakConcurrent: 2 });
  });

  it('el cliente del portal que participó ve la ficha sin la lista de invitados', async () => {
    const result = await getMeetingSummary(
      { tenantId: TENANT, meetingId: MEETING, userId: CLIENT, isPortal: true, recordingVisible: false },
      deps(m(), events(), invitations()),
    );
    expect(result.isSuccess && result.value.invitedNotJoined).toEqual([]);
    expect(result.isSuccess && result.value.participants).toHaveLength(2);
  });

  it('quien no participó recibe Meeting.NotParticipant', async () => {
    const result = await getMeetingSummary(
      { tenantId: TENANT, meetingId: MEETING, userId: 'stranger', isPortal: false, recordingVisible: true },
      deps(m(), events()),
    );
    expect(!result.isSuccess && result.error.code).toBe('Meeting.NotParticipant');
  });
});

describe('listMeetingInvitations — nombre del invitado', () => {
  it('completa el nombre desde el directorio cuando la invitación solo guardó el id', async () => {
    const result = await listMeetingInvitations(
      { tenantId: TENANT, meetingId: MEETING, actorUserId: HOST },
      deps(meeting([participant(HOST, { role: 'Host' })]), [], [invitation({ inviteeUserId: 'absent-1' })]),
    );
    expect(result.isSuccess && result.value.invitations.map((i) => i.inviteeName)).toEqual(['Ana Absent']);
  });
});

describe('listMeetingEvents — actividad de la ficha', () => {
  it('con activityOnly omite voz, calidad y reacciones y pagina sobre lo que se muestra', async () => {
    seq = 0;
    const log = new InMemoryMeetingEventLog();
    for (const e of [
      event(MeetingEventType.ParticipantJoined, 0, { actorUserId: HOST }),
      event(MeetingEventType.SpeakerStarted, 1, { actorUserId: HOST }),
      event(MeetingEventType.SfuScore, 1, { actorUserId: HOST }),
      event(MeetingEventType.ReactionSummary, 2),
      event(MeetingEventType.ScreenStarted, 3, { actorUserId: HOST }),
    ]) {
      await log.append(e);
    }
    const m = meeting([participant(HOST, { role: 'Host' })]);
    const deps = { meetings: { findById: async () => m } as unknown as MeetingRepository, meetingEvents: log };

    const all = await listMeetingEvents({ tenantId: TENANT, meetingId: MEETING, userId: HOST, take: 50 }, deps);
    const activity = await listMeetingEvents({ tenantId: TENANT, meetingId: MEETING, userId: HOST, activityOnly: true, take: 50 }, deps);

    expect(all.isSuccess && all.value.items).toHaveLength(5);
    expect(activity.isSuccess && activity.value.items.map((i) => i.type)).toEqual([MeetingEventType.ParticipantJoined, MeetingEventType.ScreenStarted]);
  });
});
