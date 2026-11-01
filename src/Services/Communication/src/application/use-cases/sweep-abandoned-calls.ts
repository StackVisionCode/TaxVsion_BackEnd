import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import type { Call } from '../../domain/calls/call.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { CallUsageRepository } from '../ports/call-usage-repository.js';
import { yearMonthUtc } from '../ports/call-usage-repository.js';
import type { CallEmptinessClock, CallRoomPresence } from '../ports/abandoned-call-ports.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PresenceService } from '../ports/presence-service.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import { stopRecordingOnEnd, type RecordingLifecycleOptionalDeps } from './recording-lifecycle.js';
import { CallEventTypes, type CallEndedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallStateDto } from '../../contracts/socket/call-socket-events.js';

export interface AbandonedCallSweepDeps extends RecordingLifecycleOptionalDeps {
  readonly recordingSessions?: RecordingSessionRepository | undefined;
  readonly calls: CallRepository;
  readonly roomPresence: CallRoomPresence;
  readonly emptinessClock: CallEmptinessClock;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly presence: PresenceService;
  readonly callUsage?: CallUsageRepository | undefined;
}

export interface AbandonedCallSweepReport {
  readonly checked: number;
  readonly abandoned: number;
}

/** Tope por pasada: el barrido corre cada pocos segundos y no debe cargar miles de filas de golpe. */
const BATCH = 200;

/**
 * Cierra las llamadas aceptadas o en curso que llevan `graceSeconds` sin ningún socket en su room.
 * Sin esto, una llamada cuyo cierre se perdió (reinicio del servidor a mitad de llamada, desconexión no
 * procesada) queda abierta para siempre y cuenta contra `maxConcurrentCalls` de la oficina.
 * Solo se miran las aceptadas hace más de `graceSeconds`: recién aceptada, la room puede estar llenándose.
 */
export async function sweepAbandonedCalls(
  input: { graceSeconds: number; now?: Date; correlationId?: string },
  deps: AbandonedCallSweepDeps,
): Promise<AbandonedCallSweepReport> {
  const now = input.now ?? new Date();
  const correlationId = input.correlationId ?? `abandoned-calls:${now.toISOString()}`;
  const graceMs = input.graceSeconds * 1000;
  const calls = await deps.calls.listOpenAcceptedBefore(new Date(now.getTime() - graceMs), BATCH);
  let abandoned = 0;
  for (const call of calls) {
    const { id, tenantId } = call.toSnapshot();
    try {
      if ((await deps.roomPresence.connectedCount(tenantId, id)) > 0) {
        await deps.emptinessClock.clear(id);
        continue;
      }
      const sinceMs = await deps.emptinessClock.emptySince(id, now.getTime());
      if (now.getTime() - sinceMs < graceMs) continue;
      if (await abandonOne(call, new Date(sinceMs), now, correlationId, deps)) abandoned += 1;
      await deps.emptinessClock.clear(id);
    } catch (err) {
      logger.warn({ err, callId: id }, 'abandoned call sweep: call skipped');
    }
  }
  return { checked: calls.length, abandoned };
}

async function abandonOne(
  call: Call,
  lastSeenAt: Date,
  now: Date,
  correlationId: string,
  deps: AbandonedCallSweepDeps,
): Promise<boolean> {
  if (!call.abandon({ lastSeenAt, now }).isSuccess) return false;
  await deps.calls.save(call);
  const after = call.toSnapshot();
  await stopRecordingOnEnd(deps, { tenantId: after.tenantId, scope: 'Call', scopeId: after.id, reason: 'CallEnded', correlationId });
  const endedAt = after.endedAtUtc ?? now;
  const endedEvent: CallEndedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.Ended,
    tenantId: after.tenantId,
    correlationId,
    occurredOnUtc: now.toISOString(),
    callId: after.id,
    callerUserId: after.callerUserId,
    calleeUserId: after.calleeUserId,
    kind: after.kind,
    endReason: 'Abandoned',
    durationSeconds: after.durationSeconds ?? 0,
    endedAtUtc: endedAt.toISOString(),
    recordingFileId: after.recordingFileId,
  };
  await deps.publisher.enqueue(endedEvent);
  if (after.durationSeconds) {
    await deps.callUsage
      ?.addSeconds({ tenantId: after.tenantId, yearMonth: yearMonthUtc(endedAt), seconds: after.durationSeconds })
      .catch(() => undefined);
  }
  const state: CallStateDto = {
    callId: after.id,
    status: after.status,
    endReason: after.endReason,
    durationSeconds: after.durationSeconds,
    updatedAtUtc: after.updatedAtUtc.toISOString(),
  };
  // A cada usuario y no a la room (que está vacía): si alguien sigue con la llamada en pantalla, se entera.
  for (const userId of [after.callerUserId, after.calleeUserId]) {
    deps.emitter.emitToUser({
      tenantId: after.tenantId,
      userId,
      event: CallSocketEvents.StateChanged,
      envelope: { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload: state },
    });
    await deps.presence.clearBusy({ tenantId: after.tenantId, userId, sourceId: after.id }).catch(() => undefined);
  }
  return true;
}
