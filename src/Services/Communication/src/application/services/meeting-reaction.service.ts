import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { MeetingSocketEvents, type MeetingReactionReceivedDto } from '../../contracts/socket/meeting-socket-events.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { MeetingReactionLimiter } from '../ports/meeting-reaction-limiter.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from '../use-cases/meeting-event-log-writer.js';
import type { MeetingReactionAggregator } from './meeting-reaction-aggregator.js';

/**
 * F5 (D-50): una reaccion efimera. Orden a proposito: politica (apagada / fuera del catalogo) ->
 * cupos (un cliente roto no cuesta lecturas de BD) -> participante Joined -> agregado por minuto ->
 * difusion volatil a la sala. Nunca se persiste la reaccion individual; la fila `reaction.summary`
 * del minuto cerrado la escribe este mismo servicio o el cierre de la reunion.
 */
export interface SendMeetingReactionCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly userId: string;
  readonly emoji: string;
  readonly correlationId: string;
  readonly now?: Date;
}

export interface SendMeetingReactionDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly reactionLimiter: MeetingReactionLimiter;
  readonly reactionAggregator: MeetingReactionAggregator;
  readonly emitter: RealtimeEmitter;
}

export async function sendMeetingReaction(cmd: SendMeetingReactionCommand, deps: SendMeetingReactionDeps): Promise<Result<void>> {
  const effective = await deps.effectiveSettings.resolve(cmd.tenantId);
  const policy = effective.media.reactions;
  if (!policy.enabled) {
    return Result.fail(makeError('Meeting.ReactionsDisabled', 'Reactions are turned off for this office.'));
  }
  if (!policy.catalog.includes(cmd.emoji)) {
    return Result.fail(makeError('Meeting.ReactionNotInCatalog', 'That reaction is not available.'));
  }
  const userOk = await deps.reactionLimiter.consumeForUser({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    userId: cmd.userId,
    maxPer10s: policy.perUserPer10s,
  });
  if (!userOk) return Result.fail(makeError('Meeting.ReactionRateLimited', 'Too many reactions, slow down.'));
  const meetingOk = await deps.reactionLimiter.consumeForMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    maxPerMinute: policy.perMeetingPerMinute,
  });
  if (!meetingOk) return Result.fail(makeError('Meeting.ReactionRateLimited', 'The room is receiving too many reactions.'));

  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const me = meeting.toSnapshot().participants.find((p) => p.userId === cmd.userId && p.status === 'Joined');
  if (meeting.status !== 'Live' || !me) {
    return Result.fail(makeError('Meeting.NotParticipant', 'Not a joined participant.'));
  }

  const at = cmd.now ?? new Date();
  const closed = deps.reactionAggregator.record(cmd.meetingId, cmd.emoji, at);
  if (closed) {
    await recordMeetingEvent(deps, {
      tenantId: cmd.tenantId,
      meetingId: cmd.meetingId,
      type: MeetingEventType.ReactionSummary,
      payload: { minuteUtc: closed.minuteUtc, counts: closed.counts },
      correlationId: cmd.correlationId,
      at,
    });
  }
  const dto: MeetingReactionReceivedDto = {
    meetingId: cmd.meetingId,
    userId: cmd.userId,
    displayName: me.displayName,
    emoji: cmd.emoji,
    at: at.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    event: MeetingSocketEvents.ReactionReceived,
    envelope: { eventId: randomUUID(), correlationId: cmd.correlationId, emittedAtUtc: at.toISOString(), payload: dto },
    volatile: true,
  });
  return Result.okVoid();
}
