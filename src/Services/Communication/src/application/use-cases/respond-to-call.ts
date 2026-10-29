import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { CallEventTypes, type CallAcceptedEvent, type CallEndedEvent } from '../../contracts/events/call-events.js';
import type { CallStateDto, CallPeerDto } from '../../contracts/socket/call-socket-events.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';

/**
 * Comandos: accept / reject / cancel. Comparten pipeline (validar principal,
 * mutar aggregate, emitir DTO comun) — separadas por dominio pero aqui las
 * factorizamos en una sola funcion para no duplicar el skeleton.
 */

export interface RespondCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
  readonly actorDisplayName: string;
  readonly action: 'accept' | 'reject' | 'cancel';
}

export interface RespondResult {
  readonly state: CallStateDto;
  readonly peer: CallPeerDto | null;
  /** Participantes — para que el handler enrute el estado terminal a sus user rooms (el callee que aún
   * NO aceptó no está en el room de la call; sin esto su modal de "incoming" no se cierra al cancelar). */
  readonly callerUserId: string;
  readonly calleeUserId: string;
}

export interface RespondCallDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  /** F5: foto del peer al aceptar. Opcional (sin el, el peer sale sin `avatarFileId`). */
  readonly avatars?: AvatarResolver;
}

export async function respondToCall(
  command: RespondCommand,
  deps: RespondCallDeps,
): Promise<Result<RespondResult>> {
  const scope = `call.${command.action}`;
  const reservation = await deps.idempotency.tryReserve<RespondResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope,
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope,
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const now = new Date();
  const mutation =
    command.action === 'accept'
      ? call.accept({ byUserId: command.actorUserId, calleeDisplayName: command.actorDisplayName, now })
      : command.action === 'reject'
        ? call.reject({ byUserId: command.actorUserId, now })
        : call.cancel({ byUserId: command.actorUserId, now });

  if (!mutation.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope,
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(mutation.error);
  }

  await deps.calls.save(call);
  const snapshot = call.toSnapshot();

  if (command.action === 'accept') {
    const acceptedEvent: CallAcceptedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.Accepted,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: (snapshot.acceptedAtUtc ?? now).toISOString(),
      callId: snapshot.id,
      callerUserId: snapshot.callerUserId,
      calleeUserId: snapshot.calleeUserId,
      kind: snapshot.kind,
      acceptedAtUtc: (snapshot.acceptedAtUtc ?? now).toISOString(),
      ringTimeMs: snapshot.acceptedAtUtc
        ? snapshot.acceptedAtUtc.getTime() - snapshot.ringingAtUtc.getTime()
        : 0,
    };
    await deps.publisher.enqueue(acceptedEvent);
  }

  if (command.action === 'reject' || command.action === 'cancel') {
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
      endReason: snapshot.endReason ?? (command.action === 'reject' ? 'Rejected' : 'Cancelled'),
      durationSeconds: snapshot.durationSeconds ?? 0,
      endedAtUtc: (snapshot.endedAtUtc ?? now).toISOString(),
      recordingFileId: snapshot.recordingFileId,
    };
    await deps.publisher.enqueue(endedEvent);
  }

  const state: CallStateDto = {
    callId: snapshot.id,
    status: snapshot.status,
    endReason: snapshot.endReason,
    durationSeconds: snapshot.durationSeconds,
    updatedAtUtc: snapshot.updatedAtUtc.toISOString(),
  };
  const calleeParticipant = snapshot.participants.find((p) => p.role === 'Callee');
  const avatars =
    command.action === 'accept' && calleeParticipant
      ? await resolveAvatars(deps.avatars, [calleeParticipant.userId])
      : undefined;
  const peer: CallPeerDto | null =
    command.action === 'accept' && calleeParticipant
      ? {
          callId: snapshot.id,
          peerUserId: calleeParticipant.userId,
          displayName: calleeParticipant.displayName,
          ...(avatars ? { avatarFileId: avatars.of(calleeParticipant.userId) } : {}),
          role: 'Callee',
          joinOrder: calleeParticipant.joinOrder,
          isPolite: true,
        }
      : null;

  const result: RespondResult = {
    state,
    peer,
    callerUserId: snapshot.callerUserId,
    calleeUserId: snapshot.calleeUserId,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope,
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
