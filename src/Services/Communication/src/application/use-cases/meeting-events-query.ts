import { Result, makeError } from '../../domain/shared/result.js';
import { MEETING_ACTIVITY_TYPES, MeetingEventScope } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { MeetingEventLogRepository } from '../ports/meeting-event-log-repository.js';

export interface MeetingEventDto {
  readonly seq: number;
  readonly at: string;
  readonly type: string;
  readonly actorUserId: string | null;
  readonly targetUserId: string | null;
  readonly payload: Readonly<Record<string, unknown>> | null;
}

export interface ListMeetingEventsQuery {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly afterSeq?: number;
  /** Solo la actividad legible (sin voz, calidad ni media). */
  readonly activityOnly?: boolean;
  readonly take: number;
}

/** Ficha del meeting (T-32): solo quien participó (o el host) lee su log; paginación incremental por `seq`. */
export async function listMeetingEvents(
  query: ListMeetingEventsQuery,
  deps: { meetings: MeetingRepository; meetingEvents: MeetingEventLogRepository },
): Promise<Result<{ items: MeetingEventDto[]; nextAfterSeq: number | null }>> {
  const meeting = await deps.meetings.findById(query.tenantId, query.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  if (!meeting.isKnownParticipant(query.userId)) {
    return Result.fail(makeError('Meeting.NotParticipant', 'Only participants can read the meeting events.'));
  }
  const entries = await deps.meetingEvents.list({
    tenantId: query.tenantId,
    scope: MeetingEventScope.Meeting,
    scopeId: query.meetingId,
    ...(query.afterSeq !== undefined ? { afterSeq: query.afterSeq } : {}),
    ...(query.activityOnly ? { types: MEETING_ACTIVITY_TYPES } : {}),
    take: query.take,
  });
  const items = entries.map((e) => ({
    seq: e.seq,
    at: e.at.toISOString(),
    type: e.type,
    actorUserId: e.actorUserId,
    targetUserId: e.targetUserId,
    payload: e.payload,
  }));
  const last = items[items.length - 1];
  return Result.ok({ items, nextAfterSeq: items.length === query.take && last ? last.seq : null });
}
