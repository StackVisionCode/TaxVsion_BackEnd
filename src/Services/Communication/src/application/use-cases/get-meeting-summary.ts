import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventScope, MeetingEventType, type MeetingEventLogEntry } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingParticipantSnapshot } from '../../domain/meetings/meeting-participant.js';
import type { MeetingSnapshot } from '../../domain/meetings/meeting.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingEventLogRepository } from '../ports/meeting-event-log-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { NO_AVATARS, resolveAvatars, type AvatarResolver } from './resolve-avatars.js';

export interface GetMeetingSummaryQuery {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  /** El cliente del portal no ve emails ni invitados, y la grabación solo si la oficina la hace visible. */
  readonly isPortal: boolean;
  readonly recordingVisible: boolean;
  readonly now?: Date;
}

export interface MeetingSummaryParticipantDto {
  readonly userId: string;
  readonly displayName: string;
  readonly avatarFileId: string | null;
  readonly role: string;
  readonly actorType: string;
  readonly firstJoinedAtUtc: string | null;
  readonly lastLeftAtUtc: string | null;
  /** Suma de todas sus sesiones (entra y sale varias veces). */
  readonly attendedSeconds: number;
  readonly sessions: number;
  readonly speakingSeconds: number;
  readonly screenShares: number;
  readonly handsRaised: number;
}

export interface MeetingSummaryInviteeDto {
  readonly kind: string;
  readonly displayName: string;
}

export interface MeetingSummaryDto {
  readonly meeting: {
    readonly id: string;
    readonly title: string;
    readonly description: string | null;
    readonly status: string;
    readonly shortCode: string;
    readonly hostUserId: string;
    readonly hostName: string | null;
    readonly scheduledForUtc: string | null;
    readonly scheduledDurationMinutes: number | null;
    readonly startedAtUtc: string | null;
    readonly endedAtUtc: string | null;
    readonly durationSeconds: number | null;
    readonly endReason: string | null;
    readonly maxParticipants: number;
    readonly transcriptFileId: string | null;
    readonly recordingState: string | null;
  };
  readonly participants: readonly MeetingSummaryParticipantDto[];
  /** Invitados con cuenta o guests que no entraron; vacío para quien no es host/cohost. */
  readonly invitedNotJoined: readonly MeetingSummaryInviteeDto[];
  readonly totals: {
    readonly attendees: number;
    readonly peakConcurrent: number;
    readonly screenShares: number;
    readonly handsRaised: number;
    readonly reactions: number;
  };
}

export interface GetMeetingSummaryDeps {
  readonly meetings: MeetingRepository;
  readonly meetingEvents: MeetingEventLogRepository;
  readonly recordingSessions: Pick<RecordingSessionRepository, 'findByScope'>;
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserIds'>;
  readonly avatars?: AvatarResolver;
}

/** Lote por lectura del log; la ficha lee todo el log de la reunión. */
const EVENTS_PAGE = 500;

/**
 * Ficha de una reunión (pasada o en curso): resumen, quién estuvo y cuánto, quién no entró y totales.
 * Mismo permiso que el log (T-32): host, cohost o quien participó.
 */
export async function getMeetingSummary(
  query: GetMeetingSummaryQuery,
  deps: GetMeetingSummaryDeps,
): Promise<Result<MeetingSummaryDto>> {
  const meeting = await deps.meetings.findById(query.tenantId, query.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const snapshot = meeting.toSnapshot();
  const canManage = query.userId === snapshot.hostUserId || meeting.isCohost(query.userId);
  if (!canManage && !meeting.isKnownParticipant(query.userId)) {
    return Result.fail(makeError('Meeting.NotParticipant', 'Only participants can read the meeting details.'));
  }

  const events = await readAllEvents(deps.meetingEvents, query.tenantId, query.meetingId);
  const closeAt = snapshot.endedAtUtc ?? query.now ?? new Date();
  const attendance = summarizeAttendance(snapshot.participants, events, closeAt);

  const attendees = snapshot.participants.flatMap((p) => {
    const presence = attendance.byUser.get(p.userId);
    return presence ? [{ participant: p, presence }] : [];
  });
  const avatars = (await resolveAvatars(deps.avatars, attendees.map((a) => a.participant.userId))) ?? NO_AVATARS;
  const participants = attendees
    .map((a) => toParticipantDto(a.participant, a.presence, avatars.of(a.participant.userId)))
    .sort((a, b) => (a.firstJoinedAtUtc ?? '').localeCompare(b.firstJoinedAtUtc ?? ''));

  const invitedNotJoined = canManage && !query.isPortal ? await listInvitedNotJoined(meeting.id, snapshot, attendance, deps) : [];
  const hostName = await resolveHostName(snapshot, deps.userDirectory);
  const recording = query.recordingVisible
    ? await deps.recordingSessions.findByScope(query.tenantId, RecordingScope.Meeting, snapshot.id)
    : null;

  return Result.ok({
    meeting: {
      id: snapshot.id,
      title: snapshot.title,
      description: snapshot.description,
      status: snapshot.status,
      shortCode: snapshot.shortCode,
      hostUserId: snapshot.hostUserId,
      hostName,
      scheduledForUtc: snapshot.scheduledForUtc?.toISOString() ?? null,
      scheduledDurationMinutes: snapshot.scheduledDurationMinutes,
      startedAtUtc: snapshot.startedAtUtc?.toISOString() ?? null,
      endedAtUtc: snapshot.endedAtUtc?.toISOString() ?? null,
      durationSeconds: snapshot.durationSeconds,
      endReason: snapshot.endReason,
      maxParticipants: snapshot.maxParticipants,
      transcriptFileId: snapshot.transcriptFileId,
      recordingState: recording ? recording.toSnapshot().state : null,
    },
    participants,
    invitedNotJoined,
    totals: {
      attendees: participants.length,
      peakConcurrent: attendance.peakConcurrent,
      screenShares: participants.reduce((sum, p) => sum + p.screenShares, 0),
      handsRaised: participants.reduce((sum, p) => sum + p.handsRaised, 0),
      reactions: attendance.reactions,
    },
  });
}

async function readAllEvents(
  repository: MeetingEventLogRepository,
  tenantId: string,
  meetingId: string,
): Promise<MeetingEventLogEntry[]> {
  const all: MeetingEventLogEntry[] = [];
  let afterSeq: number | undefined;
  for (;;) {
    const page = await repository.list({
      tenantId,
      scope: MeetingEventScope.Meeting,
      scopeId: meetingId,
      ...(afterSeq !== undefined ? { afterSeq } : {}),
      take: EVENTS_PAGE,
    });
    all.push(...page);
    const last = page[page.length - 1];
    if (page.length < EVENTS_PAGE || !last) return all;
    afterSeq = last.seq;
  }
}

export interface UserAttendance {
  firstJoinedAt: Date;
  lastLeftAt: Date | null;
  attendedMs: number;
  sessions: number;
  speakingMs: number;
  screenShares: number;
  handsRaised: number;
}

export interface AttendanceSummary {
  readonly byUser: ReadonlyMap<string, UserAttendance>;
  readonly peakConcurrent: number;
  readonly reactions: number;
}

const SESSION_START: ReadonlySet<string> = new Set([MeetingEventType.ParticipantJoined, MeetingEventType.ParticipantRejoined]);
const SESSION_END_BY_ACTOR: ReadonlySet<string> = new Set([MeetingEventType.ParticipantLeft]);
const SESSION_END_BY_TARGET: ReadonlySet<string> = new Set([
  MeetingEventType.ParticipantForcedLeave,
  MeetingEventType.ParticipantRemoved,
]);

/**
 * Sesiones por persona a partir del log: entra (joined/rejoined, o admitted por el host) y sale
 * (left, forced_leave, removed). Una sesión abierta se cierra al fin de la reunión. Si alguien no
 * tiene eventos (log incompleto), se usa su fila: joinedAt → leftAt.
 */
export function summarizeAttendance(
  participants: readonly MeetingParticipantSnapshot[],
  events: readonly MeetingEventLogEntry[],
  closeAt: Date,
): AttendanceSummary {
  const byUser = new Map<string, UserAttendance>();
  const openSession = new Map<string, Date>();
  const speakingSince = new Map<string, Date>();
  let concurrent = 0;
  let peakConcurrent = 0;
  let reactions = 0;

  const entry = (userId: string, at: Date): UserAttendance => {
    let found = byUser.get(userId);
    if (!found) {
      found = { firstJoinedAt: at, lastLeftAt: null, attendedMs: 0, sessions: 0, speakingMs: 0, screenShares: 0, handsRaised: 0 };
      byUser.set(userId, found);
    }
    return found;
  };
  const stopSpeaking = (userId: string, at: Date): void => {
    const since = speakingSince.get(userId);
    if (!since) return;
    const current = byUser.get(userId);
    if (current) current.speakingMs += Math.max(0, at.getTime() - since.getTime());
    speakingSince.delete(userId);
  };
  const start = (userId: string, at: Date): void => {
    if (openSession.has(userId)) return;
    const current = entry(userId, at);
    current.sessions += 1;
    openSession.set(userId, at);
    concurrent += 1;
    peakConcurrent = Math.max(peakConcurrent, concurrent);
  };
  const end = (userId: string, at: Date): void => {
    const since = openSession.get(userId);
    const current = byUser.get(userId);
    if (!since || !current) return;
    stopSpeaking(userId, at);
    current.attendedMs += Math.max(0, at.getTime() - since.getTime());
    current.lastLeftAt = at;
    openSession.delete(userId);
    concurrent -= 1;
  };

  const ordered = [...events].sort((a, b) => a.seq - b.seq);
  for (const e of ordered) {
    if (SESSION_START.has(e.type) && e.actorUserId) start(e.actorUserId, e.at);
    else if (e.type === MeetingEventType.ParticipantAdmitted && e.targetUserId) start(e.targetUserId, e.at);
    else if (SESSION_END_BY_ACTOR.has(e.type) && e.actorUserId) end(e.actorUserId, e.at);
    else if (SESSION_END_BY_TARGET.has(e.type) && e.targetUserId) end(e.targetUserId, e.at);
    else if (e.type === MeetingEventType.SpeakerStarted && e.actorUserId && openSession.has(e.actorUserId)) {
      if (!speakingSince.has(e.actorUserId)) speakingSince.set(e.actorUserId, e.at);
    } else if (e.type === MeetingEventType.SpeakerStopped && e.actorUserId) stopSpeaking(e.actorUserId, e.at);
    else if (e.type === MeetingEventType.ScreenStarted && e.actorUserId) entry(e.actorUserId, e.at).screenShares += 1;
    else if (e.type === MeetingEventType.HandRaised && e.actorUserId) entry(e.actorUserId, e.at).handsRaised += 1;
    else if (e.type === MeetingEventType.ReactionSummary) reactions += sumCounts(e.payload?.['counts']);
  }
  for (const userId of [...openSession.keys()]) end(userId, closeAt);

  // Respaldo por fila para quien entró sin dejar rastro en el log.
  for (const p of participants) {
    if (!p.joinedAtUtc) continue;
    const current = byUser.get(p.userId);
    if (current && current.sessions > 0) continue;
    const left = p.leftAtUtc ?? closeAt;
    const fallback = current ?? entry(p.userId, p.joinedAtUtc);
    fallback.firstJoinedAt = p.joinedAtUtc;
    fallback.sessions = 1;
    fallback.attendedMs = Math.max(0, left.getTime() - p.joinedAtUtc.getTime());
    fallback.lastLeftAt = left;
    peakConcurrent = Math.max(peakConcurrent, 1);
  }
  // Pantalla o mano sin sesión (log sin join) no cuenta como asistencia.
  for (const [userId, current] of byUser) {
    if (current.sessions === 0) byUser.delete(userId);
  }
  return { byUser, peakConcurrent, reactions };
}

function sumCounts(counts: unknown): number {
  if (!counts || typeof counts !== 'object') return 0;
  return Object.values(counts as Record<string, unknown>).reduce<number>(
    (sum, value) => sum + (typeof value === 'number' && Number.isFinite(value) ? value : 0),
    0,
  );
}

function toParticipantDto(
  p: MeetingParticipantSnapshot,
  a: UserAttendance,
  avatarFileId: string | null,
): MeetingSummaryParticipantDto {
  return {
    userId: p.userId,
    displayName: p.displayName,
    avatarFileId,
    role: p.role,
    actorType: p.actorType,
    firstJoinedAtUtc: a.firstJoinedAt.toISOString(),
    lastLeftAtUtc: a.lastLeftAt?.toISOString() ?? null,
    attendedSeconds: Math.round(a.attendedMs / 1000),
    sessions: a.sessions,
    speakingSeconds: Math.round(a.speakingMs / 1000),
    screenShares: a.screenShares,
    handsRaised: a.handsRaised,
  };
}

async function listInvitedNotJoined(
  meetingId: string,
  snapshot: MeetingSnapshot,
  attendance: AttendanceSummary,
  deps: GetMeetingSummaryDeps,
): Promise<MeetingSummaryInviteeDto[]> {
  const invitations = (await deps.meetings.listInvitationsByMeeting(snapshot.tenantId, meetingId)).map((i) => i.toSnapshot());
  const missing = invitations.filter(
    (i) => !i.revokedAtUtc && !i.usedAtUtc && !(i.inviteeUserId && attendance.byUser.has(i.inviteeUserId)),
  );
  const names = await directoryNames(
    missing.flatMap((i) => (!i.inviteeName && i.inviteeUserId ? [i.inviteeUserId] : [])),
    deps.userDirectory,
  );
  const seen = new Set<string>();
  const result: MeetingSummaryInviteeDto[] = [];
  for (const i of missing) {
    const key = i.inviteeUserId ?? i.inviteeEmail ?? i.id;
    if (seen.has(key)) continue;
    seen.add(key);
    const displayName =
      i.inviteeName || (i.inviteeUserId ? names.get(i.inviteeUserId.toLowerCase()) : undefined) || i.inviteeEmail || 'Guest';
    result.push({ kind: i.inviteeKind, displayName });
  }
  return result;
}

async function resolveHostName(
  snapshot: MeetingSnapshot,
  directory: GetMeetingSummaryDeps['userDirectory'],
): Promise<string | null> {
  const asParticipant = snapshot.participants.find((p) => p.userId === snapshot.hostUserId);
  if (asParticipant) return asParticipant.displayName;
  return (await directoryNames([snapshot.hostUserId], directory)).get(snapshot.hostUserId.toLowerCase()) ?? null;
}

/** Nombres de la proyección de usuarios (empleados y cuentas de portal), por userId en minúsculas. */
export async function directoryNames(
  userIds: readonly string[],
  directory: GetMeetingSummaryDeps['userDirectory'],
): Promise<Map<string, string>> {
  const names = new Map<string, string>();
  if (userIds.length === 0 || !directory?.findByUserIds) return names;
  for (const row of await directory.findByUserIds([...new Set(userIds)])) {
    if (row.displayName) names.set(row.userId.toLowerCase(), row.displayName);
  }
  return names;
}
