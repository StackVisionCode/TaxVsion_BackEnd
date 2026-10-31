import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Call } from '../../domain/calls/call.js';
import type { CallKind } from '../../domain/calls/call-kind.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import { type CallUsageRepository, yearMonthUtc } from '../ports/call-usage-repository.js';
import { CallEventTypes, type CallStartedEvent } from '../../contracts/events/call-events.js';

/**
 * Comando: iniciar una llamada. Idempotente por (tenantId, caller, clientKey).
 * Reglas:
 *   1. Chat habilitado (mismo settings). CallsEnabled / VideoCallsEnabled se
 *      valida contra `TenantCommunicationSettings.CallsEnabled|VideoCallsEnabled`
 *      cuando el settings-provider los agregue (Fase 6). Fase 2 usa Chat como
 *      guarda unica para no bloquear el flujo.
 *   2. Caller != callee.
 *   3. Un cliente del portal nunca llama a otro cliente del portal: la oficina es
 *      el unico interlocutor, igual que en el chat directo.
 *   4. Publica CallStarted.
 */

export interface InitiateCallCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly kind: CallKind;
  readonly caller: { userId: string; displayName: string; actorType: string };
  // actorType null = el destinatario todavia no esta en el directorio local: no se puede
  // demostrar que no es otro cliente, asi que la llamada se rechaza (fail-closed).
  readonly callee: { userId: string; displayName: string; actorType: string | null };
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
  /** F4: `callsEnabled`/`videoCallsEnabled`/`maxConcurrentCalls`/`maxMonthlyMinutes` del resolver (INV-12 H-3). */
  readonly effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined;
  readonly callUsage?: CallUsageRepository | undefined;
  /** D-58: un cliente del portal llama con su CustomerId persistido en el participante. */
  readonly customerPortalAccounts?: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'> | undefined;
}

export async function initiateCall(
  command: InitiateCallCommand,
  deps: InitiateCallDeps,
): Promise<Result<InitiateCallResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Call.Disabled', 'Communication is disabled for this tenant.'));
  }

  // F4: limites del plan/oficina aplicados en el servidor (nunca solo en el front).
  if (deps.effectiveSettings) {
    const effective = await deps.effectiveSettings.resolve(command.tenantId);
    if (!effective.calls.enabled) {
      return Result.fail(makeError('Calls.Disabled', 'Calls are disabled for this office.'));
    }
    if (command.kind === 'Video' && !effective.calls.videoEnabled) {
      return Result.fail(makeError('Calls.VideoDisabled', 'Video calls are disabled for this office.'));
    }
    if (effective.calls.maxConcurrentCalls !== null) {
      const open = await deps.calls.countOpenForTenant(command.tenantId);
      if (open >= effective.calls.maxConcurrentCalls) {
        return Result.fail(makeError('Call.LimitReached', 'concurrent'));
      }
    }
    if (effective.calls.maxMonthlyMinutes !== null && deps.callUsage) {
      const used = await deps.callUsage.getSeconds({ tenantId: command.tenantId, yearMonth: yearMonthUtc(new Date()) });
      if (used >= effective.calls.maxMonthlyMinutes * 60) {
        return Result.fail(makeError('Call.LimitReached', 'monthly'));
      }
    }
  }

  if (command.caller.actorType === 'CustomerPortal' && command.callee.actorType !== 'TenantEmployee'
    && command.callee.actorType !== 'TenantAdmin' && command.callee.actorType !== 'PlatformAdmin') {
    return Result.fail(
      makeError('Call.CustomerToCustomerNotAllowed', 'Clients can only call the office.'),
    );
  }

  const reservation = await deps.idempotency.tryReserve<InitiateCallResult>({
    tenantId: command.tenantId,
    userId: command.caller.userId,
    scope: 'call.initiate',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const callerCustomerId =
    command.caller.actorType === 'CustomerPortal' && deps.customerPortalAccounts
      ? ((await deps.customerPortalAccounts.findActiveByUserId(command.caller.userId).catch(() => null))?.customerId ?? null)
      : null;
  const callResult = Call.initiate({
    tenantId: command.tenantId,
    kind: command.kind,
    caller: { userId: command.caller.userId, displayName: command.caller.displayName, customerId: callerCustomerId },
    callee: { userId: command.callee.userId, displayName: command.callee.displayName },
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
