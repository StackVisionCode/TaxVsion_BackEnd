import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { CallEventTypes, type CallEndedEvent } from '../../contracts/events/call-events.js';
import type { CallStateDto } from '../../contracts/socket/call-socket-events.js';
import type { CallEndReason } from '../../domain/calls/call.js';

/**
 * Comando: terminar una llamada activa. Solo caller o callee. Emite
 * CallEnded al bus para que Analytics y Notification lo consuman.
 */

export interface EndCallCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
  readonly reason?: CallEndReason;
}

export interface EndCallResult {
  readonly state: CallStateDto;
}

export interface EndCallDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
}

export async function endCall(
  command: EndCallCommand,
  deps: EndCallDeps,
): Promise<Result<EndCallResult>> {
  const reservation = await deps.idempotency.tryReserve<EndCallResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.end',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.end',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const now = new Date();
  const endResult = command.reason
    ? call.end({ byUserId: command.actorUserId, reason: command.reason, now })
    : call.end({ byUserId: command.actorUserId, now });
  if (!endResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.end',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(endResult.error);
  }

  await deps.calls.save(call);
  const snapshot = call.toSnapshot();

  const endedEvent: CallEndedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.Ended,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: (snapshot.endedAtUtc ?? now).toISOString(),
    callId: snapshot.id,
    callerUserId: snapshot.callerUserId,
    calleeUserId: snapshot.calleeUserId,
    kind: snapshot.kind,
    endReason: snapshot.endReason ?? 'Hangup',
    durationSeconds: snapshot.durationSeconds ?? 0,
    endedAtUtc: (snapshot.endedAtUtc ?? now).toISOString(),
    recordingFileId: snapshot.recordingFileId,
  };
  await deps.publisher.enqueue(endedEvent);

  const result: EndCallResult = {
    state: {
      callId: snapshot.id,
      status: snapshot.status,
      endReason: snapshot.endReason,
      durationSeconds: snapshot.durationSeconds,
      updatedAtUtc: snapshot.updatedAtUtc.toISOString(),
    },
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.end',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
