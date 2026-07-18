import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Call } from '../../domain/calls/call.js';
import type { CallKind } from '../../domain/calls/call-kind.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { CallEventTypes, type CallStartedEvent } from '../../contracts/events/call-events.js';

/**
 * Comando: iniciar una llamada. Idempotente por (tenantId, caller, clientKey).
 * Reglas:
 *   1. Chat habilitado (mismo settings). CallsEnabled / VideoCallsEnabled se
 *      valida contra `TenantCommunicationSettings.CallsEnabled|VideoCallsEnabled`
 *      cuando el settings-provider los agregue (Fase 6). Fase 2 usa Chat como
 *      guarda unica para no bloquear el flujo.
 *   2. Caller != callee.
 *   3. Publica CallStarted.
 */

export interface InitiateCallCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly kind: CallKind;
  readonly caller: { userId: string; displayName: string };
  readonly callee: { userId: string; displayName: string };
  readonly conversationId?: string | null;
  readonly recordingRequested?: boolean;
}

export interface InitiateCallResult {
  readonly callId: string;
  readonly ringingAtUtc: string;
}

export interface InitiateCallDeps {
  readonly calls: CallRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
}

export async function initiateCall(
  command: InitiateCallCommand,
  deps: InitiateCallDeps,
): Promise<Result<InitiateCallResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Call.Disabled', 'Communication is disabled for this tenant.'));
  }

  const reservation = await deps.idempotency.tryReserve<InitiateCallResult>({
    tenantId: command.tenantId,
    userId: command.caller.userId,
    scope: 'call.initiate',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const callResult = Call.initiate({
    tenantId: command.tenantId,
    kind: command.kind,
    caller: command.caller,
    callee: command.callee,
    conversationId: command.conversationId ?? null,
    recordingRequested: command.recordingRequested ?? false,
  });
  if (!callResult.isSuccess) {
    await deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.caller.userId,
      scope: 'call.initiate',
      clientKey: command.clientKey,
      token: reservation.token,
    });
    return Result.fail(callResult.error);
  }
  const call = callResult.value;

  const snapshot = call.toSnapshot();
  const startedEvent: CallStartedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.Started,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: snapshot.ringingAtUtc.toISOString(),
    callId: snapshot.id,
    kind: snapshot.kind,
    callerUserId: snapshot.callerUserId,
    calleeUserId: snapshot.calleeUserId,
    conversationId: snapshot.conversationId,
    ringingAtUtc: snapshot.ringingAtUtc.toISOString(),
  };

  await deps.calls.save(call);
  await deps.publisher.enqueue(startedEvent);

  const result: InitiateCallResult = {
    callId: snapshot.id,
    ringingAtUtc: snapshot.ringingAtUtc.toISOString(),
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.caller.userId,
    scope: 'call.initiate',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
