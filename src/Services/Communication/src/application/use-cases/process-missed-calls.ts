import { randomUUID } from 'node:crypto';
import { Call } from '../../domain/calls/call.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PresenceService } from '../ports/presence-service.js';
import { CallEventTypes, type CallMissedEvent } from '../../contracts/events/call-events.js';

/**
 * Background job (Fase 2): revisa llamadas en Ringing con edad > timeoutSeconds
 * y las marca MissedCall + publica CommunicationMissedCall. Idempotente por
 * naturaleza: si el estado ya cambio (accept/reject/cancel) el guard del
 * aggregate lo rechaza.
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

export async function processMissedCalls(
  input: ProcessMissedCallsInput,
  deps: { calls: CallRepository; publisher: IntegrationEventPublisher; presence: PresenceService },
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
