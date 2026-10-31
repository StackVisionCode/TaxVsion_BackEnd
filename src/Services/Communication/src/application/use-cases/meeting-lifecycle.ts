import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import { MeetingEventTypes, type MeetingStartedEvent } from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingListChangedDto } from '../../contracts/socket/meeting-socket-events.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import { finalizeEndedMeeting, type FinalizeEndedMeetingDeps } from './finalize-ended-meeting.js';

export interface StartMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly audioDefault?: boolean;
  readonly videoDefault?: boolean;
}

export interface StartMeetingDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  /** Máximo de duración vigente de la oficina al arrancar (D-34/D-35): queda fijado en la reunión. */
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  /** Opcional (best-effort): empuja `meeting.started` a invitados/participantes. */
  readonly emitter?: RealtimeEmitter | undefined;
}

export async function startMeeting(
  cmd: StartMeetingCommand,
  deps: StartMeetingDeps,
): Promise<Result<{ startedAtUtc: string; endsAtUtc: string; maxDurationMinutes: number }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const effective = await deps.effectiveSettings.resolve(cmd.tenantId);
  const result = meeting.start({
    hostUserId: cmd.hostUserId,
    maxDurationMinutes: effective.meeting.maxDurationMinutes,
    ...(cmd.audioDefault !== undefined ? { audioDefault: cmd.audioDefault } : {}),
    ...(cmd.videoDefault !== undefined ? { videoDefault: cmd.videoDefault } : {}),
    now,
  });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();
  const endsAtUtc = snapshot.endsAtUtc ?? now;

  const event: MeetingStartedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Started,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    hostUserId: cmd.hostUserId,
    startedAtUtc: now.toISOString(),
    strategy: snapshot.strategy,
  };
  await deps.publisher.enqueue(event);
  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: MeetingEventType.Started,
    actorUserId: cmd.hostUserId,
    payload: { maxDurationMinutes: snapshot.maxDurationMinutes, endsAtUtc: endsAtUtc.toISOString() },
    correlationId: cmd.correlationId,
    at: now,
  });

  // Realtime: los invitados/participantes ven el meeting pasar a "live" (join disponible) sin
  // recargar. Se excluye al host (que ya parchea su fila localmente). Best-effort.
  if (deps.emitter) {
    const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, cmd.meetingId);
    const inviteeUserIds = invitations
      .map((inv) => inv.toSnapshot())
      .filter((s) => s.revokedAtUtc === null && s.inviteeUserId !== null)
      .map((s) => s.inviteeUserId!);
    const targets = new Set<string>([...inviteeUserIds, ...snapshot.participants.map((p) => p.userId)]);
    targets.delete(cmd.hostUserId);
    const dto: MeetingListChangedDto = { meetingId: cmd.meetingId };
    for (const userId of targets) {
      deps.emitter.emitToUser({
        tenantId: cmd.tenantId,
        userId,
        event: MeetingSocketEvents.Started,
        envelope: {
          eventId: randomUUID(),
          correlationId: cmd.correlationId,
          emittedAtUtc: now.toISOString(),
          payload: dto,
        },
      });
    }
  }

  return Result.ok({
    startedAtUtc: now.toISOString(),
    endsAtUtc: endsAtUtc.toISOString(),
    maxDurationMinutes: snapshot.maxDurationMinutes ?? effective.meeting.maxDurationMinutes,
  });
}

export interface EndMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly byUserId: string;
}

/**
 * End explícito de host o cohost (D-07). El cierre ordenado (grabación, SFU, busy, avisos a la sala y
 * a las listas, evento con `reason: Host`) es el mismo que usan el scheduler y la sucesión.
 */
export async function endMeeting(
  cmd: EndMeetingCommand,
  deps: FinalizeEndedMeetingDeps,
): Promise<Result<{ endedAtUtc: string; durationSeconds: number }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const now = new Date();
  const result = meeting.end({ byUserId: cmd.byUserId, now });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();
  await finalizeEndedMeeting(
    { tenantId: cmd.tenantId, correlationId: cmd.correlationId, snapshot, actorUserId: cmd.byUserId },
    deps,
  );
  return Result.ok({ endedAtUtc: now.toISOString(), durationSeconds: snapshot.durationSeconds ?? 0 });
}
