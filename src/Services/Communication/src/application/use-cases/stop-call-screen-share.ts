import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallScreenShareStoppedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallScreenShareStoppedDto } from '../../contracts/socket/call-socket-events.js';

export interface StopCallScreenShareCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
}

export interface StopCallScreenShareResult {
  readonly callId: string;
  readonly startedAtUtc: string;
  readonly stoppedAtUtc: string;
  readonly durationSeconds: number;
}

export interface StopCallScreenShareDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

/**
 * Solo el actor que empezo el share puede pararlo (mismo criterio que
 * attachCallRecording en Fase 4). El aggregate ya calcula `durationSeconds`
 * a partir del `screenShareStartedAtUtc` persistido — no depende de logs.
 */
export async function stopCallScreenShare(
  cmd: StopCallScreenShareCommand,
  deps: StopCallScreenShareDeps,
): Promise<Result<StopCallScreenShareResult>> {
  const reservation = await deps.idempotency.tryReserve<StopCallScreenShareResult>({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.screen_share.stop',
    clientKey: cmd.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: cmd.tenantId,
      userId: cmd.actorUserId,
      scope: 'call.screen_share.stop',
      clientKey: cmd.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(cmd.tenantId, cmd.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const stopResult = call.stopScreenShare({ actorUserId: cmd.actorUserId });
  if (!stopResult.isSuccess) {
    await release();
    return Result.fail(stopResult.error);
  }
  await deps.calls.save(call);

  const startedAtUtc = stopResult.value.startedAtUtc.toISOString();
  const stoppedAtUtc = stopResult.value.stoppedAtUtc.toISOString();
  const durationSeconds = stopResult.value.durationSeconds;

  const event: CallScreenShareStoppedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.ScreenShareStopped,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: stoppedAtUtc,
    callId: cmd.callId,
    sharingUserId: cmd.actorUserId,
    startedAtUtc,
    stoppedAtUtc,
    durationSeconds,
  };
  await deps.publisher.enqueue(event);

  const dto: CallScreenShareStoppedDto = {
    callId: cmd.callId,
    sharingUserId: cmd.actorUserId,
    startedAtUtc,
    stoppedAtUtc,
    durationSeconds,
  };
  deps.emitter.emitToCall({
    tenantId: cmd.tenantId,
    callId: cmd.callId,
    event: CallSocketEvents.ScreenShareStopped,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: stoppedAtUtc,
      payload: dto,
    },
  });

  const result: StopCallScreenShareResult = { callId: cmd.callId, startedAtUtc, stoppedAtUtc, durationSeconds };
  await deps.idempotency.commit({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.screen_share.stop',
    clientKey: cmd.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
