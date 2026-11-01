import type { AnalyticsCounters, AnalyticsRepository } from '../ports/analytics-repository.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { AnalyticsRecorder } from '../services/analytics-recorder.js';
import { RecorderEventTypes, RecordingComposedPayloadSchema } from '../../contracts/events/recording-events.js';

type Increments = Partial<AnalyticsCounters>;

/**
 * Consumers propios que alimentan el snapshot diario. Cierra CRIT legacy: los
 * reportes NUNCA se calculan escaneando tablas operacionales (Conversation,
 * Message, Call, Meeting) — aqui se acumulan contadores por dia.
 *
 * F10 (ADD-08 T-71): contadores de plataforma (audio/video, resultado de la llamada, segundos sin
 * truncar, mesh/SFU, participantes e invitados, reconexiones, fallos de transporte, calidad,
 * grabaciones, transcripts e invitaciones) y picos de concurrencia. El dia sale en la zona de la
 * plataforma y cada fila guarda el plan de la oficina.
 */
export function bindAnalyticsConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { analytics: AnalyticsRepository; recorder?: AnalyticsRecorder },
): void {
  const recorder = deps.recorder ?? new AnalyticsRecorder({ analytics: deps.analytics, timeZone: async () => 'UTC' });
  const on = (eventType: string, increments: (env: IncomingEnvelope) => Increments | null, peaks = false): void => {
    register(eventType, async (env) => {
      const values = increments(env);
      if (values && Object.keys(values).length > 0) await recorder.count(env.tenantId, env.occurredOnUtc, values);
      if (peaks) await recorder.observePeaks(env.tenantId, env.occurredOnUtc);
    });
  };

  on('communication.chat.conversation_started.v1', () => ({ conversationsStarted: 1 }));
  on('communication.chat.message_sent.v1', () => ({ messagesSent: 1 }));

  // ---- Llamadas ----
  on('communication.call.started.v1', (env) => {
    const kind = getString(env.payload, 'kind', 'Kind');
    return { callsStarted: 1, ...(kind === 'Video' ? { videoCallsStarted: 1 } : { audioCallsStarted: 1 }) };
  });
  on('communication.call.upgraded_to_video.v1', () => ({ callsUpgradedToVideo: 1 }));
  on('communication.call.ended.v1', (env) => {
    const duration = Math.max(0, getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0);
    return {
      callsEnded: 1,
      callMinutes: Math.floor(duration / 60),
      callSeconds: duration,
      ...callOutcome(getString(env.payload, 'endReason', 'EndReason')),
    };
  });
  // Una perdida solo publica `call.missed` (no `ended`): se cuenta aqui y no se duplica.
  on('communication.call.missed.v1', () => ({ missedCalls: 1 }));

  // ---- Reuniones ----
  on('communication.meeting.scheduled.v1', () => ({ meetingsScheduled: 1 }));
  on(
    'communication.meeting.started.v1',
    (env) => {
      const strategy = getString(env.payload, 'strategy', 'Strategy');
      return {
        meetingsStarted: 1,
        ...(strategy === 'Sfu' ? { meetingsSfu: 1 } : strategy === 'Mesh' ? { meetingsMesh: 1 } : {}),
      };
    },
    true,
  );
  on('communication.meeting.ended.v1', (env) => {
    const duration = Math.max(0, getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0);
    const reason = getString(env.payload, 'reason', 'Reason');
    return {
      meetingsEnded: 1,
      meetingMinutes: Math.floor(duration / 60),
      meetingSeconds: duration,
      ...(reason && SYSTEM_END_REASONS.has(reason) ? { meetingsEndedBySystem: 1 } : {}),
    };
  });
  on('communication.meeting.cancelled.v1', () => ({ meetingsCancelled: 1 }));
  on('communication.meeting.expired.v1', () => ({ meetingsExpired: 1 }));

  // La admision desde la sala de espera es la entrada de ese participante (no publica `joined`).
  for (const eventType of ['communication.meeting.participant_joined.v1', 'communication.meeting.participant_admitted.v1']) {
    on(
      eventType,
      (env) => ({ participantsJoined: 1, ...(getString(env.payload, 'actorType', 'ActorType') === 'Guest' ? { guestsJoined: 1 } : {}) }),
      true,
    );
  }
  on('communication.meeting.participant_left.v1', (env) => ({
    participantSeconds: Math.max(0, getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0),
  }));
  on('communication.meeting.participant_rejoined.v1', () => ({ rejoins: 1 }), true);
  on('communication.meeting.participant_forced_leave.v1', (env) =>
    getString(env.payload, 'reason', 'Reason') === 'Grace' ? { participantsDroppedAfterGrace: 1 } : null,
  );
  on('communication.meeting.transport_failed.v1', () => ({ transportFailures: 1 }));
  on('communication.meeting.quality_summary.v1', (env) => qualityIncrements(env.payload));

  // ---- Grabaciones y transcripts (reuniones y llamadas) ----
  for (const scope of ['meeting', 'call'] as const) {
    on(`communication.${scope}.recording_started.v1`, () => ({ recordingsStarted: 1 }));
    on(`communication.${scope}.recording_ready.v1`, (env) => ({
      recordingsReady: 1,
      recordingSeconds: Math.max(0, getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0),
    }));
    // `recording_validation_failed` termina en `recording_failed`: solo este cuenta.
    on(`communication.${scope}.recording_failed.v1`, () => ({ recordingsFailed: 1 }));
    on(`communication.${scope}.transcript_ready.v1`, (env) => {
      const audio = Math.max(0, getNumber(env.payload, 'durationSeconds', 'DurationSeconds') ?? 0);
      const factor = getNumber(env.payload, 'realtimeFactor', 'RealtimeFactor');
      return {
        transcriptsReady: 1,
        transcriptAudioSeconds: audio,
        // realtimeFactor = segundos de whisper / segundos de audio.
        ...(factor !== undefined && factor > 0 ? { transcriptProcessingSeconds: Math.round(audio * factor) } : {}),
      };
    });
    on(`communication.${scope}.transcript_failed.v1`, () => ({ transcriptsFailed: 1 }));
  }
  // Bytes del compuesto server-side (el del cliente no informa tamaño).
  on(RecorderEventTypes.Composed, (env) => {
    const parsed = RecordingComposedPayloadSchema.safeParse(env.payload);
    return parsed.success ? { recordingBytes: parsed.data.composedSizeBytes } : null;
  });

  // ---- Invitaciones ----
  on('communication.meeting.invitation_created.v1', () => ({ invitationsCreated: 1 }));
  on('communication.meeting.invitation_used.v1', () => ({ invitationsUsed: 1 }));
  on('communication.meeting.invitation_expired.v1', () => ({ invitationsExpired: 1 }));
  on('communication.meeting.invitation_revoked.v1', () => ({ invitationsRevoked: 1 }));
}

/** Fines decididos por el sistema (tiempo, sala vacia, retiro de la oficina); `Backfill` no cuenta. */
const SYSTEM_END_REASONS: ReadonlySet<string> = new Set(['MaxDurationReached', 'NoParticipants', 'Offboarding']);

function callOutcome(reason: string | undefined): Increments {
  switch (reason) {
    case 'Hangup':
      return { callsCompleted: 1 };
    case 'MaxDurationReached':
      return { callsCompleted: 1, callsEndedByLimit: 1 };
    case 'IceFailed':
    case 'Abandoned': // quedó abierta sin nadie conectado: se perdió, no terminó bien
      return { callsFailed: 1 };
    case 'Rejected':
      return { callsRejected: 1 };
    case 'Cancelled':
      return { callsCancelled: 1 };
    default:
      return {};
  }
}

const QUALITY_LEVEL_COUNTERS: Record<string, keyof AnalyticsCounters> = {
  Excellent: 'qualityExcellentReports',
  Good: 'qualityGoodReports',
  Fair: 'qualityFairReports',
  Poor: 'qualityPoorReports',
  Disconnected: 'qualityDisconnectedReports',
};

function qualityIncrements(payload: Record<string, unknown>): Increments {
  const result: Increments = { qualitySummaries: 1 };
  const levels = payload['levels'];
  if (levels && typeof levels === 'object') {
    for (const [level, count] of Object.entries(levels as Record<string, unknown>)) {
      const counter = QUALITY_LEVEL_COUNTERS[level];
      if (counter && typeof count === 'number' && count > 0) result[counter] = (result[counter] ?? 0) + count;
    }
  }
  const relay = getNumber(payload, 'relayReports', 'RelayReports');
  if (relay && relay > 0) result.relayReports = relay;
  return result;
}

function getString(src: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'string' && v.length > 0) return v;
  }
  return undefined;
}

function getNumber(src: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'number' && Number.isFinite(v)) return v;
    if (typeof v === 'string') {
      const p = Number.parseFloat(v);
      if (Number.isFinite(p)) return p;
    }
  }
  return undefined;
}
