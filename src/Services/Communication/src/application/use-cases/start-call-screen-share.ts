import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallScreenShareStartedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallScreenShareStartedDto } from '../../contracts/socket/call-socket-events.js';

export interface StartCallScreenShareCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
}

export interface StartCallScreenShareResult {
  readonly callId: string;
  readonly startedAtUtc: string;
}

export interface StartCallScreenShareDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

/**
 * Screen share con senal dedicada (no piggy-back en media_status). El
 * frontend agrega la track de pantalla al PeerConnection y renegocia
 * (offer/answer) — el server solo persiste `screenSharing=true` +
 * `screenShareStartedAtUtc` en el participante actor y publica el evento
 * para que analytics/notificaciones lo detecten sin tener que interpretar
 * media_status.
 */
export async function startCallScreenShare(
  cmd: StartCallScreenShareCommand,
  deps: StartCallScreenShareDeps,
): Promise<Result<StartCallScreenShareResult>> {
  const reservation = await deps.idempotency.tryReserve<StartCallScreenShareResult>({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.screen_share.start',
    clientKey: cmd.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: cmd.tenantId,
      userId: cmd.actorUserId,
      scope: 'call.screen_share.start',
      clientKey: cmd.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(cmd.tenantId, cmd.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const startResult = call.startScreenShare({ actorUserId: cmd.actorUserId });
  if (!startResult.isSuccess) {
    await release();
    return Result.fail(startResult.error);
  }
  await deps.calls.save(call);

  const startedAtUtc = startResult.value.startedAtUtc.toISOString();
  const event: CallScreenShareStartedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.ScreenShareStarted,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: startedAtUtc,
    callId: cmd.callId,
    sharingUserId: cmd.actorUserId,
    startedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const dto: CallScreenShareStartedDto = {
    callId: cmd.callId,
    sharingUserId: cmd.actorUserId,
    startedAtUtc,
  };
  deps.emitter.emitToCall({
    tenantId: cmd.tenantId,
    callId: cmd.callId,
    event: CallSocketEvents.ScreenShareStarted,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: startedAtUtc,
      payload: dto,
    },
  });

  const result: StartCallScreenShareResult = { callId: cmd.callId, startedAtUtc };
  await deps.idempotency.commit({
    tenantId: cmd.tenantId,
    userId: cmd.actorUserId,
    scope: 'call.screen_share.start',
    clientKey: cmd.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
