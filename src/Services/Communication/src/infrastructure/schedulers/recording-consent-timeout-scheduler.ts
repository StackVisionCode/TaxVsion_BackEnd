import { randomUUID } from 'node:crypto';
import { logger } from '../logger/logger.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import { startMeetingRecording } from '../../application/use-cases/start-meeting-recording.js';
import { startCallRecording } from '../../application/use-cases/start-call-recording.js';
import type { MeetingRepository } from '../../application/ports/meeting-repository.js';
import type { CallRepository } from '../../application/ports/call-repository.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../../application/ports/recording-repository.js';
import type { SettingsRepository } from '../../application/ports/settings-repository.js';
import type { IntegrationEventPublisher } from '../../application/ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import { MeetingSocketEvents, type MeetingRecordingStateChangedDto } from '../../contracts/socket/meeting-socket-events.js';
import { CallSocketEvents, type CallRecordingStateChangedDto } from '../../contracts/socket/call-socket-events.js';
import { MeetingEventTypes, type MeetingRecordingFailedEvent } from '../../contracts/events/meeting-events.js';
import { CallEventTypes, type CallRecordingFailedEvent } from '../../contracts/events/call-events.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * Cada `intervalSeconds` busca RecordingSession en 'Requesting' con mas de
 * `meetingTimeoutSeconds` (meetings, default 30s) / `callTimeoutSeconds`
 * (calls, Fase Backend 4: default 15s — mas estricto, un callee que no
 * responde se trata como rejection) de antiguedad y resuelve la policy —
 * reusa startMeetingRecording/startCallRecording (misma evaluacion que los
 * respond-*-recording-consent.ts respectivos) para no duplicar logica. Si la
 * policy no da luz verde ni siquiera al timeout (tipicamente
 * AllAcceptedRequired sin unanimidad), la sesion se marca Failed — sin esto
 * quedaria varada en Requesting para siempre, bloqueando cualquier intento
 * futuro de grabar ese meeting/call (RecordingSession es @@unique por
 * [Scope, ScopeId], una sola fila en toda la vida del meeting/call).
 *
 * Mismo patron de lock distribuido que missed-call-scheduler.ts. Un unico
 * tick/lock resuelve ambos scopes (Meeting y Call) — no hace falta un
 * scheduler separado por scope.
 */
const LOCK_KEY = 'comm:lock:recording-consent-timeout-scheduler';

export interface RecordingConsentTimeoutSchedulerConfig {
  readonly intervalSeconds: number;
  readonly meetingTimeoutSeconds: number;
  readonly callTimeoutSeconds: number;
}

export interface RecordingConsentTimeoutSchedulerDeps {
  readonly meetings: MeetingRepository;
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly tenantSettings: SettingsRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly lock: RedisDistributedLock;
}

export function startRecordingConsentTimeoutScheduler(
  config: RecordingConsentTimeoutSchedulerConfig,
  deps: RecordingConsentTimeoutSchedulerDeps,
): { stop(): void } {
  const handle = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        await runOnceForMeetings(config, deps);
        await runOnceForCalls(config, deps);
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'RecordingConsentTimeoutScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);

  return {
    stop() {
      clearInterval(handle);
    },
  };
}

async function runOnceForMeetings(
  config: RecordingConsentTimeoutSchedulerConfig,
  deps: RecordingConsentTimeoutSchedulerDeps,
): Promise<void> {
  const olderThanUtc = new Date(Date.now() - config.meetingTimeoutSeconds * 1000);
  const staleSessions = await deps.recordingSessions.listStaleRequesting(olderThanUtc, RecordingScope.Meeting);
  if (staleSessions.length === 0) return;

  let started = 0;
  let failed = 0;
  for (const session of staleSessions) {
    const snap = session.toSnapshot();
    const startResult = await startMeetingRecording(
      {
        tenantId: snap.tenantId,
        correlationId: randomUUID(),
        meetingId: snap.scopeId,
        actorUserId: snap.requestedByUserId,
      },
      deps,
    );
    if (startResult.isSuccess) {
      started += 1;
      continue;
    }

    // Policy nunca se va a satisfacer con lo que hay (o el meeting/session ya
    // no existe) — falla permanentemente para liberar el slot @@unique.
    const meeting = await deps.meetings.findById(snap.tenantId, snap.scopeId);
    if (!meeting) continue;
    const failResult = meeting.failRecording({ session, reason: 'ConsentTimeout' });
    if (!failResult.isSuccess) continue;
    await deps.recordingSessions.save(failResult.value);
    failed += 1;

    const failedAtUtc = new Date().toISOString();
    const failedEvent: MeetingRecordingFailedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingFailed,
      tenantId: snap.tenantId,
      correlationId: '',
      occurredOnUtc: failedAtUtc,
      meetingId: snap.scopeId,
      reason: 'ConsentTimeout',
      failedAtUtc,
      hostUserId: meeting.hostUserId,
    };
    await deps.publisher.enqueue(failedEvent);

    const dto: MeetingRecordingStateChangedDto = {
      meetingId: snap.scopeId,
      state: 'Failed',
      updatedAtUtc: new Date().toISOString(),
    };
    deps.emitter.emitToMeeting({
      tenantId: snap.tenantId,
      meetingId: snap.scopeId,
      event: MeetingSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });
  }

  if (started > 0 || failed > 0) {
    logger.info({ started, failed, checked: staleSessions.length }, 'RecordingConsentTimeoutScheduler: resolved stale meeting sessions');
  }
}

async function runOnceForCalls(
  config: RecordingConsentTimeoutSchedulerConfig,
  deps: RecordingConsentTimeoutSchedulerDeps,
): Promise<void> {
  const olderThanUtc = new Date(Date.now() - config.callTimeoutSeconds * 1000);
  const staleSessions = await deps.recordingSessions.listStaleRequesting(olderThanUtc, RecordingScope.Call);
  if (staleSessions.length === 0) return;

  let started = 0;
  let failed = 0;
  for (const session of staleSessions) {
    const snap = session.toSnapshot();
    const startResult = await startCallRecording(
      {
        tenantId: snap.tenantId,
        correlationId: randomUUID(),
        callId: snap.scopeId,
        actorUserId: snap.requestedByUserId,
      },
      deps,
    );
    if (startResult.isSuccess) {
      started += 1;
      continue;
    }

    // Con policy fija AllAcceptedRequired, al timeout sin unanimidad esto
    // siempre falla — el callee que no respondio se trata como rejection
    // (mas estricto que meetings). Falla permanentemente para liberar el
    // slot @@unique.
    const call = await deps.calls.findById(snap.tenantId, snap.scopeId);
    if (!call) continue;
    const failResult = call.failRecording({ session, reason: 'ConsentTimeout' });
    if (!failResult.isSuccess) continue;
    await deps.recordingSessions.save(failResult.value);
    failed += 1;

    const failedAtUtc = new Date().toISOString();
    const failedEvent: CallRecordingFailedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingFailed,
      tenantId: snap.tenantId,
      correlationId: '',
      occurredOnUtc: failedAtUtc,
      callId: snap.scopeId,
      reason: 'ConsentTimeout',
      failedAtUtc,
      callerUserId: call.callerUserId,
      calleeUserId: call.calleeUserId,
    };
    await deps.publisher.enqueue(failedEvent);

    const dto: CallRecordingStateChangedDto = {
      callId: snap.scopeId,
      state: 'Failed',
      updatedAtUtc: new Date().toISOString(),
    };
    deps.emitter.emitToCall({
      tenantId: snap.tenantId,
      callId: snap.scopeId,
      event: CallSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });
  }

  if (started > 0 || failed > 0) {
    logger.info({ started, failed, checked: staleSessions.length }, 'RecordingConsentTimeoutScheduler: resolved stale call sessions');
  }
}
