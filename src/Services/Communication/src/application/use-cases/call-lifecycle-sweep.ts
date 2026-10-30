import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import type { Call } from '../../domain/calls/call.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { CallUsageRepository } from '../ports/call-usage-repository.js';
import { yearMonthUtc } from '../ports/call-usage-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { PresenceService } from '../ports/presence-service.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallDurationWarningEvent, type CallEndedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallDurationWarningDto, type CallStateDto } from '../../contracts/socket/call-socket-events.js';
import { DURATION_WARNING_THRESHOLDS_SECONDS } from './meeting-lifecycle-sweep.js';

export interface CallLifecycleSweepDeps {
  readonly calls: CallRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly presence: PresenceService;
  readonly callUsage?: CallUsageRepository | undefined;
}

export interface CallSweepReport {
  readonly checked: number;
  readonly warned: number;
  readonly endedByTime: number;
}

/**
 * Corte server-side por `maxCallMinutes` (INV-12 H-3, ADD-11 H-4) con el mismo patron que las
 * reuniones (T-39): avisos 10/5/1 una sola vez y fin `MaxDurationReached` para ambos. El tope lo fija
 * el `accept` desde el resolver; aqui solo se aplica.
 */
export async function sweepActiveCalls(input: { now?: Date; correlationId?: string }, deps: CallLifecycleSweepDeps): Promise<CallSweepReport> {
  const now = input.now ?? new Date();
  const correlationId = input.correlationId ?? `call-lifecycle:${now.toISOString()}`;
  const horizon = new Date(now.getTime() + Math.max(...DURATION_WARNING_THRESHOLDS_SECONDS) * 1000);
  const calls = await deps.calls.listEndingBefore(horizon);
  const report = { checked: 0, warned: 0, endedByTime: 0 };
  for (const call of calls) {
    report.checked += 1;
    try {
      const outcome = await sweepOne(call, now, correlationId, deps);
      if (outcome === 'warned') report.warned += 1;
      if (outcome === 'ended') report.endedByTime += 1;
    } catch (err) {
      logger.warn({ err, callId: call.toSnapshot().id }, 'call lifecycle sweep: call skipped');
    }
  }
  return report;
}

function envelope<T>(correlationId: string, now: Date, payload: T) {
  return { eventId: randomUUID(), correlationId, emittedAtUtc: now.toISOString(), payload };
}

async function sweepOne(call: Call, now: Date, correlationId: string, deps: CallLifecycleSweepDeps): Promise<'none' | 'warned' | 'ended'> {
  const remaining = call.remainingSeconds(now);
  if (remaining === null) return 'none';
  const snapshot = call.toSnapshot();

  if (remaining <= 0) {
    const ended = call.endBySystem({ reason: 'MaxDurationReached', now });
    if (!ended.isSuccess) return 'none';
    await deps.calls.save(call);
    const after = call.toSnapshot();
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
      endReason: 'MaxDurationReached',
      durationSeconds: after.durationSeconds ?? 0,
      endedAtUtc: now.toISOString(),
      recordingFileId: after.recordingFileId,
    };
    await deps.publisher.enqueue(endedEvent);
    await deps.callUsage?.addSeconds({ tenantId: after.tenantId, yearMonth: yearMonthUtc(now), seconds: after.durationSeconds ?? 0 }).catch(() => undefined);
    const state: CallStateDto = {
      callId: after.id,
      status: after.status,
      endReason: after.endReason,
      durationSeconds: after.durationSeconds,
      updatedAtUtc: after.updatedAtUtc.toISOString(),
    };
    deps.emitter.emitToCall({ tenantId: after.tenantId, callId: after.id, event: CallSocketEvents.StateChanged, envelope: envelope(correlationId, now, state) });
    await Promise.all(
      [after.callerUserId, after.calleeUserId].map((userId) =>
        deps.presence.clearBusy({ tenantId: after.tenantId, userId, sourceId: after.id }).catch(() => undefined),
      ),
    );
    return 'ended';
  }

  const threshold = [...DURATION_WARNING_THRESHOLDS_SECONDS].sort((a, b) => a - b).find((t) => remaining <= t);
  if (threshold === undefined) return 'none';
  const marked = call.markDurationWarning({ thresholdSeconds: threshold, now });
  if (!marked.isSuccess) return 'none';
  await deps.calls.save(call);
  const endsAtUtc = snapshot.endsAtUtc?.toISOString() ?? now.toISOString();
  const event: CallDurationWarningEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.DurationWarning,
    tenantId: snapshot.tenantId,
    correlationId,
    occurredOnUtc: now.toISOString(),
    callId: snapshot.id,
    remainingSeconds: remaining,
    endsAtUtc,
  };
  await deps.publisher.enqueue(event);
  const dto: CallDurationWarningDto = { callId: snapshot.id, remainingSeconds: remaining, endsAtUtc };
  deps.emitter.emitToCall({ tenantId: snapshot.tenantId, callId: snapshot.id, event: CallSocketEvents.DurationWarning, envelope: envelope(correlationId, now, dto) });
  return 'warned';
}
