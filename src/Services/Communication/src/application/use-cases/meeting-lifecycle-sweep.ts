import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import type { Meeting } from '../../domain/meetings/meeting.js';
import { MeetingEndReason } from '../../domain/meetings/meeting-enums.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { TECHNICAL_LIMITS } from '../../domain/settings/technical-limits.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import {
  MeetingEventTypes,
  type MeetingDurationWarningEvent,
  type MeetingExpiredEvent,
  type MeetingReminderEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingDurationWarningDto,
  type MeetingEndedDto,
  type MeetingReminderDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { config } from '../../infrastructure/config.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { TenantHostResolver } from '../ports/tenant-host-resolver.js';
import { buildMeetingListUrls } from './build-meeting-join-url.js';
import { closeMeetingInvitations } from './close-meeting-invitations.js';
import { resolveUserDisplayName } from './resolve-user-display-name.js';
import { finalizeEndedMeeting, type FinalizeEndedMeetingDeps } from './finalize-ended-meeting.js';
import { recordMeetingEvent } from './meeting-event-log-writer.js';

/** Umbrales de aviso antes del corte (INV-11 §13.2a): 10, 5 y 1 minuto. */
export const DURATION_WARNING_THRESHOLDS_SECONDS = [600, 300, 60] as const;

export interface LifecycleSweepDeps extends FinalizeEndedMeetingDeps {
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  /** F6 (T-64): nombre del host, zona horaria y host de la oficina para el recordatorio. */
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserId'> | undefined;
  readonly tenantHostResolver?: TenantHostResolver | undefined;
}

export interface LiveSweepReport {
  readonly checked: number;
  readonly warned: number;
  readonly endedByTime: number;
  readonly endedEmpty: number;
}

/**
 * Barrido de reuniones Live (T-39): aviso 10/5/1 una sola vez por umbral, corte al tope y fin por
 * sala vacía (minutos del resolver por oficina). Un fallo en una reunión no detiene a las demás.
 */
export async function sweepLiveMeetings(
  input: { now?: Date; take?: number; correlationId?: string },
  deps: LifecycleSweepDeps,
): Promise<LiveSweepReport> {
  const now = input.now ?? new Date();
  const correlationId = input.correlationId ?? `lifecycle:${now.toISOString()}`;
  const report = { checked: 0, warned: 0, endedByTime: 0, endedEmpty: 0 };
  const meetings = await deps.meetings.listLive({ take: input.take ?? 500 });
  for (const meeting of meetings) {
    report.checked += 1;
    try {
      const outcome = await sweepOneLiveMeeting(meeting, now, correlationId, deps);
      if (outcome === 'warned') report.warned += 1;
      if (outcome === 'endedByTime') report.endedByTime += 1;
      if (outcome === 'endedEmpty') report.endedEmpty += 1;
    } catch (err) {
      logger.warn({ err, meetingId: meeting.id }, 'lifecycle sweep: meeting skipped');
    }
  }
  return report;
}

async function sweepOneLiveMeeting(
  meeting: Meeting,
  now: Date,
  correlationId: string,
  deps: LifecycleSweepDeps,
): Promise<'none' | 'warned' | 'endedByTime' | 'endedEmpty'> {
  const remaining = meeting.remainingSeconds(now);
  if (remaining !== null && remaining <= 0) {
    await endBySystemAndFinalize(meeting, MeetingEndReason.MaxDurationReached, now, correlationId, deps);
    return 'endedByTime';
  }

  const emptySince = meeting.emptySinceUtc();
  if (emptySince) {
    const effective = await deps.effectiveSettings.resolve(meeting.tenantId);
    if (now.getTime() - emptySince.getTime() >= effective.meeting.emptyEndMinutes * 60_000) {
      await endBySystemAndFinalize(meeting, MeetingEndReason.NoParticipants, now, correlationId, deps);
      return 'endedEmpty';
    }
  }

  if (remaining === null) return 'none';
  const threshold = [...DURATION_WARNING_THRESHOLDS_SECONDS].sort((a, b) => a - b).find((t) => remaining <= t);
  if (threshold === undefined) return 'none';
  const marked = meeting.markDurationWarning({ thresholdSeconds: threshold, now });
  if (!marked.isSuccess) return 'none';
  await deps.meetings.save(meeting);
  const endsAtUtc = meeting.endsAtUtc?.toISOString() ?? now.toISOString();
  const event: MeetingDurationWarningEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.DurationWarning,
    tenantId: meeting.tenantId,
    correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: meeting.id,
    remainingSeconds: remaining,
    endsAtUtc,
  };
  await deps.publisher.enqueue(event);
  await recordMeetingEvent(deps, {
    tenantId: meeting.tenantId,
    meetingId: meeting.id,
    type: MeetingEventType.DurationWarning,
    payload: { thresholdSeconds: threshold, remainingSeconds: remaining },
    correlationId,
    at: now,
  });
  if (deps.emitter) {
    const dto: MeetingDurationWarningDto = { meetingId: meeting.id, remainingSeconds: remaining, endsAtUtc };
    deps.emitter.emitToMeeting({
      tenantId: meeting.tenantId,
      meetingId: meeting.id,
      event: MeetingSocketEvents.DurationWarning,
      envelope: { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload: dto },
    });
  }
  return 'warned';
}

export async function endBySystemAndFinalize(
  meeting: Meeting,
  reason: Exclude<MeetingEndReason, 'Host'>,
  now: Date,
  correlationId: string,
  deps: FinalizeEndedMeetingDeps,
): Promise<void> {
  const ended = meeting.endBySystem({ reason, now });
  if (!ended.isSuccess) return;
  await deps.meetings.save(meeting);
  await finalizeEndedMeeting(
    { tenantId: meeting.tenantId, correlationId, snapshot: meeting.toSnapshot(), actorUserId: null },
    deps,
  );
}

export interface ExpirySweepReport {
  readonly checked: number;
  readonly expired: number;
}

/**
 * Agendadas que nunca empezaron y vencieron (D-35): `Scheduled` → `Expired` con evento y aviso a las
 * listas. El corte de búsqueda usa el mínimo técnico; el plazo real es el de cada oficina.
 */
export async function expireStaleScheduledMeetings(
  input: { now?: Date; take?: number; correlationId?: string; dryRun?: boolean },
  deps: LifecycleSweepDeps,
): Promise<ExpirySweepReport> {
  const now = input.now ?? new Date();
  const correlationId = input.correlationId ?? `lifecycle:${now.toISOString()}`;
  const cutoffUtc = new Date(now.getTime() - TECHNICAL_LIMITS.scheduledMeetingExpiryHours.min * 3_600_000);
  const candidates = await deps.meetings.listScheduledBefore({ cutoffUtc, take: input.take ?? 500 });
  const report = { checked: candidates.length, expired: 0 };
  for (const meeting of candidates) {
    try {
      const snapshot = meeting.toSnapshot();
      const effective = await deps.effectiveSettings.resolve(meeting.tenantId);
      const anchor = snapshot.scheduledForUtc ?? snapshot.createdAtUtc;
      const dueAt = anchor.getTime() + effective.meeting.scheduledExpiryHours * 3_600_000;
      if (now.getTime() < dueAt) continue;
      report.expired += 1;
      if (input.dryRun) continue;

      const expired = meeting.expire({ now });
      if (!expired.isSuccess) continue;
      await deps.meetings.save(meeting);
      const event: MeetingExpiredEvent = {
        eventId: randomUUID(),
        eventType: MeetingEventTypes.Expired,
        tenantId: meeting.tenantId,
        correlationId,
        occurredOnUtc: now.toISOString(),
        meetingId: meeting.id,
        hostUserId: snapshot.hostUserId,
        scheduledForUtc: snapshot.scheduledForUtc ? snapshot.scheduledForUtc.toISOString() : null,
        expiredAtUtc: now.toISOString(),
      };
      await deps.publisher.enqueue(event);
      await closeMeetingInvitations({ tenantId: meeting.tenantId, meetingId: meeting.id, reason: 'Expired', correlationId, now }, deps);
      await recordMeetingEvent(deps, {
        tenantId: meeting.tenantId,
        meetingId: meeting.id,
        type: MeetingEventType.Expired,
        correlationId,
        at: now,
      });
      if (deps.emitter) {
        const dto: MeetingEndedDto = { meetingId: meeting.id, reason: 'Expired' };
        for (const p of snapshot.participants) {
          deps.emitter.emitToUser({
            tenantId: meeting.tenantId,
            userId: p.userId,
            event: MeetingSocketEvents.Ended,
            envelope: { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload: dto },
          });
        }
      }
    } catch (err) {
      logger.warn({ err, meetingId: meeting.id }, 'expiry sweep: meeting skipped');
    }
  }
  return report;
}

export interface ReminderSweepReport {
  readonly checked: number;
  readonly sent: number;
}

/**
 * F6 (T-64): recordatorio T-N de reuniones agendadas, una sola vez por reunion. N sale del resolver
 * (plataforma > oficina; 0 = apagado). La ventana de busqueda usa el maximo tecnico; el instante real
 * es el de cada oficina. Si al barrer la hora ya paso (servicio caido), se marca sin avisar tarde.
 */
export async function sendDueMeetingReminders(
  input: { now?: Date; take?: number; correlationId?: string },
  deps: LifecycleSweepDeps,
): Promise<ReminderSweepReport> {
  const now = input.now ?? new Date();
  const correlationId = input.correlationId ?? `reminder:${now.toISOString()}`;
  const untilUtc = new Date(now.getTime() + TECHNICAL_LIMITS.meetingReminderMinutesBefore.max * 60_000);
  const candidates = await deps.meetings.listScheduledForReminder({ untilUtc, take: input.take ?? 500 });
  const report = { checked: candidates.length, sent: 0 };
  for (const meeting of candidates) {
    try {
      const snapshot = meeting.toSnapshot();
      if (!snapshot.scheduledForUtc) continue;
      const effective = await deps.effectiveSettings.resolve(meeting.tenantId);
      const minutesBefore = effective.meeting.reminderMinutesBefore;
      if (minutesBefore <= 0) continue;
      if (now.getTime() < snapshot.scheduledForUtc.getTime() - minutesBefore * 60_000) continue;
      const marked = meeting.markReminderSent({ now });
      if (!marked.isSuccess) continue;
      await deps.meetings.save(meeting);
      if (now.getTime() >= snapshot.scheduledForUtc.getTime()) continue;
      report.sent += 1;

      const invitations = (await deps.meetings.listInvitationsByMeeting(meeting.tenantId, meeting.id))
        .map((inv) => inv.toSnapshot())
        .filter((s) => s.revokedAtUtc === null && s.closedAtUtc === null);
      const inviteeUserIds = [...new Set(invitations.flatMap((s) => (s.inviteeUserId ? [s.inviteeUserId] : [])))];
      const invitedEmails = invitations.flatMap((s) => (s.inviteeUserId === null && s.inviteeEmail ? [s.inviteeEmail] : []));
      const participantUserIds = snapshot.participants.map((p) => p.userId);
      const host = (await deps.tenantHostResolver?.resolveHost(meeting.tenantId)) ?? null;
      const timeZone = (await deps.tenantHostResolver?.resolveTimeZone?.(meeting.tenantId)) ?? null;
      const urls = buildMeetingListUrls({
        host,
        fallbackBaseUrl: config.meetingInvitations.frontendBaseUrl,
        portalPathPrefix: config.meetingInvitations.portalPathPrefix,
      });
      const hostDisplayName = await resolveUserDisplayName(
        deps.userDirectory,
        snapshot.hostUserId,
        snapshot.participants.find((p) => p.userId === snapshot.hostUserId)?.displayName,
      );
      const event: MeetingReminderEvent = {
        eventId: randomUUID(),
        eventType: MeetingEventTypes.Reminder,
        tenantId: meeting.tenantId,
        correlationId,
        occurredOnUtc: now.toISOString(),
        meetingId: meeting.id,
        meetingTitle: snapshot.title,
        shortCode: snapshot.shortCode,
        scheduledForUtc: snapshot.scheduledForUtc.toISOString(),
        durationMinutes: snapshot.scheduledDurationMinutes ?? snapshot.maxDurationMinutes,
        minutesBefore,
        hostUserId: snapshot.hostUserId,
        hostDisplayName,
        timeZone,
        participantUserIds,
        inviteeUserIds,
        invitedEmails,
        staffJoinUrl: urls.staffJoinUrl,
        portalJoinUrl: urls.portalJoinUrl,
      };
      await deps.publisher.enqueue(event);
      await recordMeetingEvent(deps, {
        tenantId: meeting.tenantId,
        meetingId: meeting.id,
        type: MeetingEventType.ReminderSent,
        payload: { minutesBefore, users: participantUserIds.length + inviteeUserIds.length, emails: invitedEmails.length },
        correlationId,
        at: now,
      });
      if (deps.emitter) {
        const dto: MeetingReminderDto = {
          meetingId: meeting.id,
          title: snapshot.title,
          scheduledForUtc: snapshot.scheduledForUtc.toISOString(),
          minutesBefore,
        };
        for (const userId of new Set<string>([...participantUserIds, ...inviteeUserIds])) {
          deps.emitter.emitToUser({
            tenantId: meeting.tenantId,
            userId,
            event: MeetingSocketEvents.Reminder,
            envelope: { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload: dto },
          });
        }
      }
    } catch (err) {
      logger.warn({ err, meetingId: meeting.id }, 'reminder sweep: meeting skipped');
    }
  }
  return report;
}
