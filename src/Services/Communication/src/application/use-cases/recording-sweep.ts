import { logger } from '../../infrastructure/logger/logger.js';
import { RecordingScope, RecordingStopReason } from '../../domain/recording/recording-session-state.js';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { SettingsRepository } from '../ports/settings-repository.js';
import type { FileDeletionRequester, FileDeletionTarget } from '../ports/file-deletion-requester.js';
import {
  failRecording,
  handleRecordingComposed,
  markRecordingFileAvailable,
  markRecordingFileRejected,
  stopRecordingBySystem,
  warnRecordingLimit,
  type RecordingLifecycleDeps,
} from './recording-lifecycle.js';
import { findRecordingTranscript } from '../services/recording-transcript-lookup.js';
import type { TranscriptArtifact } from '../../domain/recording/transcript-artifact.js';
import { markTranscriptFileAvailable, releaseRejectedTranscriptFile } from './transcript-file-status.js';

export interface RecordingSweepDeps extends RecordingLifecycleDeps {
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve' | 'platform'>;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
}

export interface RecordingSweepReport {
  readonly warned: number;
  readonly stoppedByLimit: number;
  readonly ready: number;
  readonly failed: number;
  /** Composiciones terminadas cuyo evento no llegó: se aplicó el resultado leído del recorder. */
  readonly recovered: number;
  /** MNT-01 (E-292): transcripts cuyo .txt CloudStorage confirmo / nunca registro. */
  readonly transcriptsConfirmed: number;
  readonly transcriptsReleased: number;
}

/** Estados de CloudStorage que significan "el archivo nunca estara disponible". */
const REJECTED_FILE_STATUSES = new Set(['Infected', 'Quarantined', 'ScanFailed', 'BlockedByPolicy', 'Rejected', 'Deleted']);
/** Un archivo recien subido tarda en registrarse/escanearse: no se consulta antes de este margen. */
const AVAILABILITY_CHECK_GRACE_MS = 60_000;
/** Tope duro: ninguna composición "en curso" se espera más de esto, diga lo que diga el recorder. */
const COMPOSITION_HARD_CAP_MS = 24 * 3_600_000;
/** Transcripts sin confirmar revisados por vuelta (incluye los anteriores a E-292, una sola vez). */
const TRANSCRIPT_CHECK_BATCH = 50;

/**
 * F7: barrido de grabaciones (lock Redis afuera). (1) aviso y corte por limite (T-35); (2) respaldo de
 * disponibilidad cuando el evento de CloudStorage no llega (cuota o politica descartan en silencio);
 * (3) composiciones vencidas: antes de darlas por perdidas se le pregunta al recorder. Los plazos salen
 * de la configuracion efectiva y se miden desde que empezo la composicion vigente.
 */
export async function runRecordingSweep(now: Date, deps: RecordingSweepDeps): Promise<RecordingSweepReport> {
  let warned = 0;
  let stoppedByLimit = 0;
  let ready = 0;
  let failed = 0;
  let recovered = 0;

  for (const session of await deps.recordingSessions.listRecordingWithLimit()) {
    const s = session.toSnapshot();
    try {
      if (session.isLimitReached(now)) {
        await stopRecordingBySystem(
          { tenantId: s.tenantId, scope: s.scope, scopeId: s.scopeId, reason: RecordingStopReason.LimitReached, correlationId: `recording-limit:${s.id}` },
          deps,
        );
        stoppedByLimit += 1;
        continue;
      }
      const effective = await deps.effectiveSettings.resolve(s.tenantId);
      if (session.shouldWarnLimit(now, effective.recording.limitWarningMinutes * 60)) {
        await warnRecordingLimit(session, deps, now);
        warned += 1;
      }
    } catch (err) {
      logger.warn({ err, sessionId: s.id }, 'recording limit sweep failed for session');
    }
  }

  const platform = await deps.effectiveSettings.platform();
  const availabilityTimeoutMs = (platform.recordingAvailabilityTimeoutMinutes ?? 60) * 60_000;

  for (const session of await deps.recordingSessions.listProcessing()) {
    const outcome = await checkAvailability(session, now, availabilityTimeoutMs, deps).catch((err: unknown) => {
      logger.warn({ err, sessionId: session.id }, 'recording availability check failed');
      return 'none' as const;
    });
    if (outcome === 'ready') ready += 1;
    if (outcome === 'failed') failed += 1;
  }

  for (const session of await deps.recordingSessions.listComposingSince(new Date(now.getTime() - availabilityTimeoutMs))) {
    const outcome = await settleOverdueComposition(session, now, deps).catch((err: unknown) => {
      logger.warn({ err, sessionId: session.id }, 'overdue composition check failed');
      return 'none' as const;
    });
    if (outcome === 'failed') failed += 1;
    if (outcome === 'recovered') recovered += 1;
  }

  let transcriptsConfirmed = 0;
  let transcriptsReleased = 0;
  const unconfirmed =
    (await deps.transcripts?.listReadyUnconfirmed?.(new Date(now.getTime() - AVAILABILITY_CHECK_GRACE_MS), TRANSCRIPT_CHECK_BATCH)) ?? [];
  for (const artifact of unconfirmed) {
    const outcome = await checkTranscriptFile(artifact, now, availabilityTimeoutMs, deps).catch((err: unknown) => {
      logger.warn({ err, transcriptId: artifact.toSnapshot().id }, 'transcript file check failed');
      return 'none' as const;
    });
    if (outcome === 'confirmed') transcriptsConfirmed += 1;
    if (outcome === 'released') transcriptsReleased += 1;
  }

  return { warned, stoppedByLimit, ready, failed, recovered, transcriptsConfirmed, transcriptsReleased };
}

/**
 * Composición que pasó el plazo. El reloj solo no basta para darla por perdida: un video largo o una
 * cola ocupada tardan más. Se pregunta al recorder: si sigue trabajando se espera (hasta el tope duro);
 * si ya terminó y el evento no llegó, se aplica su resultado; en cualquier otro caso falla por plazo.
 */
async function settleOverdueComposition(
  session: RecordingSession,
  now: Date,
  deps: RecordingSweepDeps,
): Promise<'failed' | 'recovered' | 'none'> {
  const s = session.toSnapshot();
  const correlationId = `recording:${s.id}`;
  const report = (await deps.capture?.compositionStatus?.(session).catch(() => null)) ?? { status: 'unknown' as const };
  if (report.status === 'done' && report.composed) {
    logger.warn({ sessionId: s.id }, 'composed event never arrived; applying the result read from the recorder');
    await handleRecordingComposed(report.composed, { tenantId: s.tenantId, correlationId }, deps);
    // Si el resultado no se pudo aplicar, sigue Composing y cae al fallo por plazo (no se reintenta sin fin).
    if (session.sessionState !== 'Composing') return 'recovered';
  }
  const elapsedMs = now.getTime() - session.composingSince().getTime();
  if (report.status === 'working' && elapsedMs < COMPOSITION_HARD_CAP_MS) {
    logger.debug({ sessionId: s.id, elapsedMinutes: Math.round(elapsedMs / 60_000) }, 'composition is overdue but the recorder is still working');
    return 'none';
  }
  logger.warn({ sessionId: s.id, recorder: report.status, elapsedMinutes: Math.round(elapsedMs / 60_000) }, 'composition timed out');
  await failRecording(session, 'CompositionTimeout', deps, correlationId);
  return 'failed';
}

/**
 * MNT-01 (E-292): respaldo del evento de CloudStorage para el .txt de un transcript. Disponible →
 * confirmado (no se vuelve a revisar); rechazado o sin registro pasado el plazo → Failed y la reunion
 * deja de apuntarlo. Sana tambien los transcripts que quedaron apuntando a un archivo inexistente.
 */
async function checkTranscriptFile(
  artifact: TranscriptArtifact,
  now: Date,
  timeoutMs: number,
  deps: RecordingSweepDeps,
): Promise<'confirmed' | 'released' | 'none'> {
  const a = artifact.toSnapshot();
  if (!a.txtFileId || !a.completedAtUtc || !deps.transcripts) return 'none';
  const statusDeps = { transcripts: deps.transcripts, meetings: deps.meetings, calls: deps.calls };
  const metadata = await deps.cloudStorageMetadata.getMetadata(a.tenantId, a.txtFileId);
  if (metadata?.status === 'Available') {
    await markTranscriptFileAvailable({ tenantId: a.tenantId, fileId: a.txtFileId }, statusDeps);
    return 'confirmed';
  }
  if (metadata?.status && REJECTED_FILE_STATUSES.has(metadata.status)) {
    await releaseRejectedTranscriptFile({ tenantId: a.tenantId, fileId: a.txtFileId, reason: metadata.status }, statusDeps);
    return 'released';
  }
  if (!metadata && now.getTime() - a.completedAtUtc.getTime() > timeoutMs) {
    await releaseRejectedTranscriptFile({ tenantId: a.tenantId, fileId: a.txtFileId, reason: 'NotRegistered' }, statusDeps);
    return 'released';
  }
  return 'none';
}

async function checkAvailability(
  session: RecordingSession,
  now: Date,
  timeoutMs: number,
  deps: RecordingSweepDeps,
): Promise<'ready' | 'failed' | 'none'> {
  const s = session.toSnapshot();
  const fileId = s.recordingFileId;
  const since = (s.composedAtUtc ?? s.stoppedAtUtc ?? s.requestedAtUtc).getTime();
  if (!fileId || now.getTime() - since < AVAILABILITY_CHECK_GRACE_MS) return 'none';
  const metadata = await deps.cloudStorageMetadata.getMetadata(s.tenantId, fileId);
  if (metadata?.status === 'Available') {
    await markRecordingFileAvailable({ tenantId: s.tenantId, fileId }, deps);
    return 'ready';
  }
  if (metadata?.status && REJECTED_FILE_STATUSES.has(metadata.status)) {
    await markRecordingFileRejected({ tenantId: s.tenantId, fileId, reason: metadata.status }, deps);
    return 'failed';
  }
  if (!metadata && now.getTime() - since > timeoutMs) {
    // CloudStorage nunca registro el archivo: cuota llena o politica (descarta sin evento).
    await markRecordingFileRejected({ tenantId: s.tenantId, fileId, reason: 'StorageRejected' }, deps);
    return 'failed';
  }
  return 'none';
}

export interface RecordingRetentionDeps extends RecordingLifecycleDeps {
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve' | 'platform'>;
  readonly settings: Pick<SettingsRepository, 'listTenantsWithContent'>;
  readonly fileDeletion: FileDeletionRequester;
}

/** Lote por vuelta: la retencion no tiene prisa y no debe competir con las reuniones. */
const RETENTION_BATCH = 50;

/**
 * F7 (T-37): (1) pistas de audio vencidas (`recordingTracksRetentionDays`, global) para todas las
 * oficinas; (2) grabaciones y transcripts vencidos solo en oficinas con la purga activa (retención efectiva). Borra por
 * evento a CloudStorage y marca la sesion; un reintento repite el pedido sin efecto adicional.
 */
export async function runRecordingRetention(now: Date, deps: RecordingRetentionDeps): Promise<{ tracks: number; recordings: number }> {
  const platform = await deps.effectiveSettings.platform();
  const tracksCutoff = new Date(now.getTime() - (platform.recordingTracksRetentionDays ?? 7) * 86_400_000);
  let tracks = 0;
  for (const session of await deps.recordingSessions.listTracksToPurge(tracksCutoff, RETENTION_BATCH)) {
    const s = session.toSnapshot();
    const files = audioTrackTargets(session);
    await deps.fileDeletion.requestDeletion({ tenantId: s.tenantId, sourceReferenceId: s.id, reason: 'retention', correlationId: `retention:${s.id}`, files });
    session.markTracksPurged(now);
    await deps.recordingSessions.save(session);
    tracks += 1;
  }

  let recordings = 0;
  for (const tenantId of await deps.settings.listTenantsWithContent()) {
    const effective = await deps.effectiveSettings.resolve(tenantId);
    if (!effective.recording.purgeEnabled) continue;
    const cutoff = new Date(now.getTime() - effective.recording.retentionDays * 86_400_000);
    for (const session of await deps.recordingSessions.listReadyToPurge(tenantId, cutoff, RETENTION_BATCH)) {
      const s = session.toSnapshot();
      const files: FileDeletionTarget[] = [];
      if (s.recordingFileId) files.push({ fileId: s.recordingFileId, ownerType: 'Communication', ownerId: s.scopeId, folderType: 'Recordings' });
      const transcript = deps.transcripts ? await findRecordingTranscript(deps.transcripts, session) : null;
      const t = transcript?.toSnapshot();
      if (t?.txtFileId) files.push({ fileId: t.txtFileId, ownerType: 'Communication', ownerId: s.scopeId, folderType: 'Transcripts' });
      if (t?.jsonFileId) files.push({ fileId: t.jsonFileId, ownerType: 'Communication', ownerId: s.scopeId, folderType: 'Transcripts' });
      if (!s.tracksPurgedAtUtc) files.push(...audioTrackTargets(session));
      await deps.fileDeletion.requestDeletion({ tenantId: s.tenantId, sourceReferenceId: s.id, reason: 'retention', correlationId: `retention:${s.id}`, files });
      session.markPurged(now);
      await deps.recordingSessions.save(session);
      if (s.scope === RecordingScope.Meeting) {
        const meeting = await deps.meetings.findById(s.tenantId, s.scopeId);
        if (meeting) {
          meeting.clearRecordingFiles({ recordingFileId: s.recordingFileId, transcriptFileId: t?.txtFileId ?? null });
          await deps.meetings.save(meeting);
        }
      } else {
        const call = await deps.calls.findById(s.tenantId, s.scopeId);
        if (call) {
          call.clearRecordingFiles({ recordingFileId: s.recordingFileId, transcriptFileId: t?.txtFileId ?? null });
          await deps.calls.save(call);
        }
      }
      recordings += 1;
    }
  }
  return { tracks, recordings };
}

function audioTrackTargets(session: RecordingSession): FileDeletionTarget[] {
  const s = session.toSnapshot();
  return (s.tracksManifest?.audioTracks ?? []).map((t) => ({
    fileId: t.fileId,
    ownerType: 'Communication' as const,
    ownerId: s.scopeId,
    folderType: 'RecordingTracks' as const,
  }));
}
