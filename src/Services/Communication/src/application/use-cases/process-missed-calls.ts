import { randomUUID } from 'node:crypto';
import { Call } from '../../domain/calls/call.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PresenceService } from '../ports/presence-service.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallMissedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallStateDto } from '../../contracts/socket/call-socket-events.js';
import type { SocketEnvelope } from '../../contracts/socket/socket-envelope.js';
import { appendCallSystemMessage, callSystemMessageBody } from './append-call-system-message.js';

/**
 * Background job (Fase 2): revisa llamadas en Ringing con edad > timeoutSeconds
 * y las marca MissedCall + publica CommunicationMissedCall. Idempotente por
 * naturaleza: si el estado ya cambio (accept/reject/cancel) el guard del
 * aggregate lo rechaza.
 *
 * Ademas notifica en tiempo real (el job es el UNICO camino a MissedCall para
 * una llamada a un par offline, ya que initiate no mira presencia):
 *   1. emite `call.state_changed` (status MissedCall) al room de la call → el
 *      caller ve "Could not reach {name}" (sin esto la llamada le rinde infinito).
 *   2. si la call tiene conversationId, escribe un mensaje System ("Missed call")
 *      a la conversacion y lo emite → queda en el historial de ambos lados.
 *
 * Scheduler concreto (RedisDistributedLock + setInterval) vive en infrastructure.
 */
export interface ProcessMissedCallsInput {
  readonly timeoutSeconds: number;
  readonly now?: Date;
}

export interface ProcessMissedCallsResult {
  readonly processed: number;
}

export interface ProcessMissedCallsDeps {
  readonly calls: CallRepository;
  readonly conversations: ConversationRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly presence: PresenceService;
  readonly emitter: RealtimeEmitter;
}

function envelope<T>(payload: T): SocketEnvelope<T> {
  return { eventId: randomUUID(), correlationId: '', emittedAtUtc: new Date().toISOString(), payload };
}

export async function processMissedCalls(
  input: ProcessMissedCallsInput,
  deps: ProcessMissedCallsDeps,
): Promise<ProcessMissedCallsResult> {
  const now = input.now ?? new Date();
  const cutoff = new Date(now.getTime() - input.timeoutSeconds * 1000);
  const candidates = await deps.calls.findRingingOlderThan(cutoff);
  let processed = 0;

  for (const snapshot of candidates) {
    const call = Call.rehydrate(snapshot);
    const markResult = call.markMissed(now);
    if (!markResult.isSuccess) continue;

    await deps.calls.save(call);
    // Fase A3 — el caller quedo marcado busy desde Initiate (dialing out);
    // como nunca hubo Accept, el callee jamas se marco busy para esta call.
    await deps.presence
      .clearBusy({ tenantId: snapshot.tenantId, userId: snapshot.callerUserId, sourceId: snapshot.id })
      .catch(() => undefined);

    const missed = call.toSnapshot();
    // 1) Notificar el cambio de estado al room de la call → el caller dispara el toast "Could not reach".
    const state: CallStateDto = {
      callId: missed.id,
      status: missed.status,
      endReason: missed.endReason,
      durationSeconds: missed.durationSeconds,
      updatedAtUtc: missed.updatedAtUtc.toISOString(),
    };
    deps.emitter.emitToCall({
      tenantId: missed.tenantId,
      callId: missed.id,
      event: CallSocketEvents.StateChanged,
      envelope: envelope(state),
    });
    // El callee que estaba sonando (online, con el modal de "incoming" abierto) NUNCA se unió al room
    // `call:{id}` (eso pasa al aceptar), así que el emitToCall de arriba no lo alcanza: hay que avisar a su
    // user room para que su modal se cierre. El caller sí está en el room, pero emitirle también es inocuo.
    for (const participantId of [snapshot.callerUserId, snapshot.calleeUserId]) {
      deps.emitter.emitToUser({
        tenantId: missed.tenantId,
        userId: participantId,
        event: CallSocketEvents.StateChanged,
        envelope: envelope(state),
      });
    }

    // 2) Registrar la llamada perdida en el historial de la conversacion (mensaje System).
    if (missed.conversationId) {
      await appendCallSystemMessage(
        {
          tenantId: missed.tenantId,
          conversationId: missed.conversationId,
          body: callSystemMessageBody({ status: 'MissedCall', kind: missed.kind }),
          now,
        },
        deps,
      ).catch(() => undefined);
    }

    const missedEvent: CallMissedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.Missed,
      tenantId: snapshot.tenantId,
      correlationId: undefined,
      occurredOnUtc: now.toISOString(),
      callId: snapshot.id,
      callerUserId: snapshot.callerUserId,
      calleeUserId: snapshot.calleeUserId,
      kind: snapshot.kind,
      ringingAtUtc: snapshot.ringingAtUtc.toISOString(),
      missedAtUtc: now.toISOString(),
    };
    await deps.publisher.enqueue(missedEvent);
    processed += 1;
  }

  return { processed };
}
