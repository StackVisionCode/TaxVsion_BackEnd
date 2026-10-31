import { describe, expect, it, vi } from 'vitest';
import { bindAnalyticsConsumers } from '../../src/application/event-handlers/analytics-consumers.js';
import { AnalyticsRecorder } from '../../src/application/services/analytics-recorder.js';
import type { AnalyticsRepository } from '../../src/application/ports/analytics-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';

const TENANT = '11111111-1111-4111-8111-111111111111';

function setup(options: { timeZone?: string; planCode?: string | null; live?: { meetings: number; participants: number } } = {}) {
  const analytics = {
    incrementCounters: vi.fn().mockResolvedValue(undefined),
    raisePeaks: vi.fn().mockResolvedValue(undefined),
    listForRange: vi.fn(),
    listAllForRange: vi.fn(),
    purgeBefore: vi.fn(),
  } satisfies AnalyticsRepository;
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const live = options.live;
  const recorder = new AnalyticsRecorder({
    analytics,
    timeZone: async () => options.timeZone ?? 'UTC',
    planCodeOf: async () => options.planCode ?? 'enterprise',
    ...(live ? { live: { countForTenant: async () => live, countPlatform: vi.fn() } } : {}),
  });
  bindAnalyticsConsumers((type, handler) => handlers.set(type, handler), { analytics, recorder });
  const deliver = async (eventType: string, payload: Record<string, unknown>, occurredOnUtc = '2026-10-31T15:00:00.000Z') => {
    const handler = handlers.get(eventType);
    if (!handler) throw new Error(`no handler for ${eventType}`);
    await handler({ eventId: 'e1', eventType, tenantId: TENANT, occurredOnUtc, payload } as IncomingEnvelope);
  };
  const lastIncrements = () => analytics.incrementCounters.mock.calls.at(-1)?.[0];
  return { analytics, deliver, lastIncrements, handlers };
}

/** F10 (ADD-08 T-71): contadores de plataforma a partir de los eventos de integración. */
describe('bindAnalyticsConsumers', () => {
  it('una llamada de video arranca contando el tipo y guarda el plan de la oficina', async () => {
    const { deliver, lastIncrements } = setup({ planCode: 'starter' });
    await deliver('communication.call.started.v1', { kind: 'Video' });
    expect(lastIncrements()).toEqual({
      tenantId: TENANT,
      day: '2026-10-31',
      increments: { callsStarted: 1, videoCallsStarted: 1 },
      planCode: 'starter',
    });
  });

  it.each([
    ['Hangup', { callsCompleted: 1 }],
    ['MaxDurationReached', { callsCompleted: 1, callsEndedByLimit: 1 }],
    ['IceFailed', { callsFailed: 1 }],
    ['Rejected', { callsRejected: 1 }],
    ['Cancelled', { callsCancelled: 1 }],
  ])('call.ended con %s: resultado y segundos exactos (sin truncar a minutos)', async (endReason, outcome) => {
    const { deliver, lastIncrements } = setup();
    await deliver('communication.call.ended.v1', { endReason, durationSeconds: 45 });
    expect(lastIncrements()?.increments).toEqual({ callsEnded: 1, callMinutes: 0, callSeconds: 45, ...outcome });
  });

  it('una reunión SFU iniciada cuenta la estrategia y sube los picos del día', async () => {
    const { deliver, lastIncrements, analytics } = setup({ live: { meetings: 2, participants: 7 } });
    await deliver('communication.meeting.started.v1', { strategy: 'Sfu' });
    expect(lastIncrements()?.increments).toEqual({ meetingsStarted: 1, meetingsSfu: 1 });
    expect(analytics.raisePeaks).toHaveBeenCalledWith({
      tenantId: TENANT,
      day: '2026-10-31',
      peaks: { peakConcurrentMeetings: 2, peakConcurrentParticipants: 7 },
    });
  });

  it('el fin por tiempo cuenta como fin del sistema; el del host no', async () => {
    const { deliver, analytics } = setup();
    await deliver('communication.meeting.ended.v1', { reason: 'MaxDurationReached', durationSeconds: 3600 });
    await deliver('communication.meeting.ended.v1', { reason: 'Host', durationSeconds: 90 });
    expect(analytics.incrementCounters.mock.calls[0]?.[0].increments).toEqual({
      meetingsEnded: 1,
      meetingMinutes: 60,
      meetingSeconds: 3600,
      meetingsEndedBySystem: 1,
    });
    expect(analytics.incrementCounters.mock.calls[1]?.[0].increments).toEqual({ meetingsEnded: 1, meetingMinutes: 1, meetingSeconds: 90 });
  });

  it('un invitado que entra (directo o admitido desde la sala de espera) cuenta como guest', async () => {
    const { deliver, analytics } = setup();
    await deliver('communication.meeting.participant_joined.v1', { actorType: 'Guest' });
    await deliver('communication.meeting.participant_admitted.v1', { actorType: 'Guest' });
    await deliver('communication.meeting.participant_joined.v1', { actorType: 'TenantEmployee' });
    const increments = analytics.incrementCounters.mock.calls.map((c) => c[0].increments);
    expect(increments).toEqual([
      { participantsJoined: 1, guestsJoined: 1 },
      { participantsJoined: 1, guestsJoined: 1 },
      { participantsJoined: 1 },
    ]);
  });

  it('solo la salida por gracia vencida cuenta como caída', async () => {
    const { deliver, analytics } = setup();
    await deliver('communication.meeting.participant_forced_leave.v1', { reason: 'Revoked' });
    expect(analytics.incrementCounters).not.toHaveBeenCalled();
    await deliver('communication.meeting.participant_forced_leave.v1', { reason: 'Grace' });
    expect(analytics.incrementCounters.mock.calls[0]?.[0].increments).toEqual({ participantsDroppedAfterGrace: 1 });
  });

  it('el resumen de calidad reparte los reportes por nivel e ignora niveles desconocidos', async () => {
    const { deliver, lastIncrements } = setup();
    await deliver('communication.meeting.quality_summary.v1', {
      levels: { Excellent: 10, Poor: 2, Weird: 5 },
      relayReports: 3,
    });
    expect(lastIncrements()?.increments).toEqual({
      qualitySummaries: 1,
      qualityExcellentReports: 10,
      qualityPoorReports: 2,
      relayReports: 3,
    });
  });

  it('grabación: lista con segundos, bytes del compuesto server-side y transcript con tiempo de proceso', async () => {
    const { deliver, analytics } = setup();
    await deliver('communication.meeting.recording_ready.v1', { durationSeconds: 120 });
    await deliver('communication.recording.composed.v1', {
      sessionId: '22222222-2222-4222-8222-222222222222',
      scope: 'Meeting',
      scopeId: '33333333-3333-4333-8333-333333333333',
      composedFileId: '44444444-4444-4444-8444-444444444444',
      composedSizeBytes: 5_000_000_000,
      format: 'mp4',
      durationSeconds: 120,
    });
    await deliver('communication.call.transcript_ready.v1', { durationSeconds: 100, realtimeFactor: 0.25 });
    const increments = analytics.incrementCounters.mock.calls.map((c) => c[0].increments);
    expect(increments).toEqual([
      { recordingsReady: 1, recordingSeconds: 120 },
      { recordingBytes: 5_000_000_000 },
      { transcriptsReady: 1, transcriptAudioSeconds: 100, transcriptProcessingSeconds: 25 },
    ]);
  });

  it('el día se corta en la zona de la plataforma', async () => {
    const { deliver, lastIncrements } = setup({ timeZone: 'America/New_York' });
    // 02:00 UTC del 31 = 22:00 del 30 en Nueva York.
    await deliver('communication.call.missed.v1', {}, '2026-10-31T02:00:00.000Z');
    expect(lastIncrements()?.day).toBe('2026-10-30');
  });

  it('cubre grabaciones y transcripts de reuniones y llamadas, e invitaciones', () => {
    const { handlers } = setup();
    for (const scope of ['meeting', 'call']) {
      for (const suffix of ['recording_started', 'recording_ready', 'recording_failed', 'transcript_ready', 'transcript_failed']) {
        expect(handlers.has(`communication.${scope}.${suffix}.v1`)).toBe(true);
      }
    }
    for (const suffix of ['created', 'used', 'expired', 'revoked']) {
      expect(handlers.has(`communication.meeting.invitation_${suffix}.v1`)).toBe(true);
    }
    // La validación fallida desemboca en recording_failed: no se cuenta dos veces.
    expect(handlers.has('communication.meeting.recording_validation_failed.v1')).toBe(false);
  });
});
