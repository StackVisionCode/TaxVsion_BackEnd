import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { ConnectionQuality } from '../../domain/calls/media-status.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from '../use-cases/meeting-event-log-writer.js';
import { CallSocketEvents, type CallPeerQualityChangedDto } from '../../contracts/socket/call-socket-events.js';
import {
  MeetingSocketEvents,
  type MeetingParticipantQualityChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import type { MeetingQualityAggregator } from './meeting-quality-aggregator.js';

/** Metricas que justifican el nivel (INV-03 §13.3): se guardan, nunca se difunden a los participantes. */
export interface ConnectionQualityMetrics {
  readonly rttMs?: number | null | undefined;
  readonly lossPct?: number | null | undefined;
  readonly jitterMs?: number | null | undefined;
  readonly viaRelay?: boolean | null | undefined;
  readonly inKbps?: number | null | undefined;
  readonly outKbps?: number | null | undefined;
  readonly fps?: number | null | undefined;
  readonly layer?: number | string | null | undefined;
}

export type MediaScope = 'Call' | 'Meeting';

export interface ReportConnectionQualityInput {
  readonly scope: MediaScope;
  readonly scopeId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly quality: ConnectionQuality;
  readonly metrics?: ConnectionQualityMetrics | undefined;
  readonly correlationId: string;
}

export interface ConnectionQualityDeps extends MeetingEventLogDeps {
  readonly calls: CallRepository;
  readonly meetings: MeetingRepository;
  readonly emitter: RealtimeEmitter;
  readonly qualityAggregator?: MeetingQualityAggregator | undefined;
}

/**
 * Servicio por Scope (ADD-11 T-76): el cliente mide y reporta su nivel; el servidor lo persiste en el
 * participante, lo deja en el MeetingEventLog con las cifras y lo difunde (solo el nivel) al par o a la
 * sala para que cada tile muestre la calidad del otro (D-39).
 */
export async function reportConnectionQuality(
  input: ReportConnectionQualityInput,
  deps: ConnectionQualityDeps,
): Promise<Result<{ changed: boolean }>> {
  const now = new Date();
  const envelope = <T>(payload: T) => ({
    eventId: randomUUID(),
    correlationId: input.correlationId,
    emittedAtUtc: now.toISOString(),
    payload,
  });

  if (input.scope === 'Call') {
    const call = await deps.calls.findById(input.tenantId, input.scopeId);
    if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));
    const before = call.toSnapshot().participants.find((p) => p.userId === input.userId)?.connectionQuality;
    const applied = call.reportConnectionQuality({ byUserId: input.userId, quality: input.quality });
    if (!applied.isSuccess) return Result.fail(applied.error);
    await deps.calls.save(call);
    const changed = before !== input.quality;
    if (changed) {
      const dto: CallPeerQualityChangedDto = { callId: input.scopeId, peerUserId: input.userId, quality: input.quality };
      deps.emitter.emitToCall({ tenantId: input.tenantId, callId: input.scopeId, event: CallSocketEvents.PeerQualityChanged, envelope: envelope(dto) });
    }
    return Result.ok({ changed });
  }

  const meeting = await deps.meetings.findById(input.tenantId, input.scopeId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const before = meeting.toSnapshot().participants.find((p) => p.userId === input.userId)?.connectionQuality;
  const applied = meeting.reportConnectionQuality({ byUserId: input.userId, quality: input.quality });
  if (!applied.isSuccess) return Result.fail(applied.error);
  await deps.meetings.save(meeting);
  deps.qualityAggregator?.recordLevel(input.scopeId, input.userId, input.quality, input.metrics?.viaRelay ?? undefined);
  const changed = before !== input.quality;
  if (!changed) return Result.ok({ changed: false });

  await recordMeetingEvent(deps, {
    tenantId: input.tenantId,
    meetingId: input.scopeId,
    type: MeetingEventType.QualityChanged,
    actorUserId: input.userId,
    payload: { quality: input.quality, ...(input.metrics ? { metrics: input.metrics } : {}) },
    correlationId: input.correlationId,
    at: now,
  });
  const dto: MeetingParticipantQualityChangedDto = { meetingId: input.scopeId, userId: input.userId, quality: input.quality };
  deps.emitter.emitToMeeting({
    tenantId: input.tenantId,
    meetingId: input.scopeId,
    event: MeetingSocketEvents.ParticipantQualityChanged,
    envelope: envelope(dto),
  });
  return Result.ok({ changed: true });
}
