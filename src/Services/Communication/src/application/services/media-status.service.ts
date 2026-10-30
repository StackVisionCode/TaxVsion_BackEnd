import { randomUUID } from 'node:crypto';
import { Result } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { updateCallMediaStatus } from '../use-cases/update-call-media-status.js';
import { updateMeetingMediaStatus } from '../use-cases/update-meeting-media-status.js';
import type { MeetingEventLogDeps } from '../use-cases/meeting-event-log-writer.js';
import type { CallMediaStatusDto } from '../../contracts/socket/call-socket-events.js';
import type { MeetingParticipantDto } from '../../contracts/socket/meeting-socket-events.js';
import {
  MeetingEventTypes,
  type MeetingScreenShareStartedEvent,
  type MeetingScreenShareStoppedEvent,
} from '../../contracts/events/meeting-events.js';
import type { MediaScope } from './connection-quality.service.js';

export interface UpdateMediaStatusInput {
  readonly scope: MediaScope;
  readonly scopeId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
  readonly correlationId: string;
}

export interface MediaStatusDeps extends MeetingEventLogDeps {
  readonly calls: CallRepository;
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
}

export type MediaStatusOutcome =
  | { readonly scope: 'Call'; readonly status: CallMediaStatusDto }
  | { readonly scope: 'Meeting'; readonly participant: MeetingParticipantDto };

/**
 * Servicio por Scope (ADD-11 T-76): un solo punto de entrada para el media-status propio. En meetings,
 * una transicion de pantalla publica los eventos de integracion simetricos a los de calls
 * (`communication.meeting.screen_share_started/stopped.v1`, INV-04 §13.4) ademas de la fila del log.
 */
export async function updateMediaStatus(input: UpdateMediaStatusInput, deps: MediaStatusDeps): Promise<Result<MediaStatusOutcome>> {
  if (input.scope === 'Call') {
    const result = await updateCallMediaStatus(
      {
        tenantId: input.tenantId,
        callId: input.scopeId,
        actorUserId: input.userId,
        audioEnabled: input.audioEnabled,
        videoEnabled: input.videoEnabled,
        screenSharing: input.screenSharing,
      },
      deps,
    );
    if (!result.isSuccess) return Result.fail(result.error);
    return Result.ok({ scope: 'Call', status: result.value.status });
  }

  const before = (await deps.meetings.findById(input.tenantId, input.scopeId))
    ?.toSnapshot()
    .participants.find((p) => p.userId === input.userId);
  const result = await updateMeetingMediaStatus(
    {
      tenantId: input.tenantId,
      meetingId: input.scopeId,
      actorUserId: input.userId,
      audioEnabled: input.audioEnabled,
      videoEnabled: input.videoEnabled,
      screenSharing: input.screenSharing,
    },
    deps,
  );
  if (!result.isSuccess) return Result.fail(result.error);

  if (before && before.screenSharing !== result.value.screenSharing) {
    const now = new Date().toISOString();
    const base = { eventId: randomUUID(), tenantId: input.tenantId, correlationId: input.correlationId, occurredOnUtc: now, meetingId: input.scopeId, sharingUserId: input.userId };
    const event: MeetingScreenShareStartedEvent | MeetingScreenShareStoppedEvent = result.value.screenSharing
      ? { ...base, eventType: MeetingEventTypes.ScreenShareStarted, startedAtUtc: now }
      : { ...base, eventType: MeetingEventTypes.ScreenShareStopped, stoppedAtUtc: now };
    await deps.publisher.enqueue(event);
  }
  return Result.ok({ scope: 'Meeting', participant: result.value });
}
