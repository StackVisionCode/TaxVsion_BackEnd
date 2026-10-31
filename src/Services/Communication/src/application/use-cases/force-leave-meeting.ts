import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type ForcedLeaveReason,
  type MeetingParticipantForcedLeaveEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingStateDto,
  type MeetingYouRemovedDto,
  type SfuProducerClosedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { leaveMeeting } from './leave-meeting.js';
import { succeedHost } from './succeed-host.js';
import { finalizeEndedMeeting, type FinalizeEndedMeetingDeps } from './finalize-ended-meeting.js';
import { recordMeetingEvent } from './meeting-event-log-writer.js';

export interface ForceLeaveMeetingDeps extends FinalizeEndedMeetingDeps {
  readonly conversations: ConversationRepository;
  readonly emitter?: RealtimeEmitter | undefined;
}

export interface ForceLeaveMeetingInput {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly reason: ForcedLeaveReason;
  readonly correlationId: string;
  /** Socket concreto que se va (revocación/`exp`): recibe `meeting.you.removed`. */
  readonly socketId?: string | undefined;
}

export interface ForceLeaveMeetingResult {
  readonly conversationId: string | null;
  /** La reunión terminó en esta misma operación (sin staff para suceder al host). */
  readonly meetingEnded: boolean;
}

/**
 * Rutina única de salida forzada (T-08): gracia vencida, sesión revocada, token vencido, retiro
 * del empleado o fin de la reunión. Saca al participante, avisa a la sala, libera su media en el SFU
 * y su "busy", deja rastro (evento + log) y, si era el host de una reunión Live, aplica la sucesión
 * (D-08) o el cierre ordenado.
 */
export async function forceLeaveMeeting(
  input: ForceLeaveMeetingInput,
  deps: ForceLeaveMeetingDeps,
): Promise<Result<ForceLeaveMeetingResult>> {
  const before = await deps.meetings.findById(input.tenantId, input.meetingId);
  if (!before) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const wasHost = before.hostUserId === input.userId;
  const wasJoined = before.isJoinedParticipant(input.userId);

  const left = await leaveMeeting(
    { tenantId: input.tenantId, correlationId: input.correlationId, meetingId: input.meetingId, userId: input.userId },
    deps,
  );
  if (!left.isSuccess) return Result.fail(left.error);
  const now = new Date();

  if (wasJoined) {
    const event: MeetingParticipantForcedLeaveEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.ParticipantForcedLeave,
      tenantId: input.tenantId,
      correlationId: input.correlationId,
      occurredOnUtc: now.toISOString(),
      meetingId: input.meetingId,
      participantUserId: input.userId,
      reason: input.reason,
      leftAtUtc: now.toISOString(),
    };
    await deps.publisher.enqueue(event);
    await recordMeetingEvent(deps, {
      tenantId: input.tenantId,
      meetingId: input.meetingId,
      type: MeetingEventType.ParticipantForcedLeave,
      actorUserId: null,
      targetUserId: input.userId,
      payload: { reason: input.reason },
      correlationId: input.correlationId,
      at: now,
    });
  }

  const envelope = <T>(payload: T) => ({
    eventId: randomUUID(),
    correlationId: input.correlationId,
    emittedAtUtc: now.toISOString(),
    payload,
  });
  if (deps.emitter) {
    deps.emitter.emitToMeeting({
      tenantId: input.tenantId,
      meetingId: input.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: input.meetingId, participant: left.value.participant, sequence: 0 }),
    });
    for (const p of deps.sfu.listProducersForUser(input.meetingId, input.userId)) {
      const closed: SfuProducerClosedDto = { meetingId: input.meetingId, userId: input.userId, producerId: p.producerId };
      deps.emitter.emitToMeeting({ tenantId: input.tenantId, meetingId: input.meetingId, event: MeetingSocketEvents.SfuProducerClosed, envelope: envelope(closed) });
    }
    if (input.socketId) {
      const removed: MeetingYouRemovedDto = { meetingId: input.meetingId, reason: input.reason };
      deps.emitter.emitToSocket?.({ socketId: input.socketId, event: MeetingSocketEvents.YouRemoved, envelope: envelope(removed) });
    }
  }
  await deps.sfu
    .closeParticipant(input.meetingId, input.userId)
    .catch((err: unknown) => logger.warn({ err, meetingId: input.meetingId, userId: input.userId }, 'sfu closeParticipant failed'));
  await deps.presence.clearBusy({ tenantId: input.tenantId, userId: input.userId, sourceId: input.meetingId }).catch(() => undefined);

  // El host se fue de verdad: sucesión (D-08) o cierre si no queda staff.
  if (wasHost && before.status === 'Live') {
    const succession = await succeedHost({ tenantId: input.tenantId, meetingId: input.meetingId, correlationId: input.correlationId }, deps);
    if (succession.isSuccess && succession.value.outcome === 'ended') {
      await finalizeEndedMeeting(
        { tenantId: input.tenantId, correlationId: input.correlationId, snapshot: succession.value.meeting.toSnapshot(), actorUserId: null },
        deps,
      );
      return Result.ok({ conversationId: left.value.conversationId, meetingEnded: true });
    }
    return Result.ok({ conversationId: left.value.conversationId, meetingEnded: false });
  }

  if (deps.emitter) {
    const after = (await deps.meetings.findById(input.tenantId, input.meetingId))?.toSnapshot();
    const state: MeetingStateDto = {
      meetingId: input.meetingId,
      status: after?.status ?? 'Live',
      isLocked: after?.isLocked ?? false,
      hostUserId: after?.hostUserId ?? before.hostUserId,
      sequence: 0,
    };
    deps.emitter.emitToMeeting({ tenantId: input.tenantId, meetingId: input.meetingId, event: MeetingSocketEvents.StateChanged, envelope: envelope(state) });
  }
  return Result.ok({ conversationId: left.value.conversationId, meetingEnded: false });
}
