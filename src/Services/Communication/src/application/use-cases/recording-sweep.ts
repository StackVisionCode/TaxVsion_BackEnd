import { logger } from '../../infrastructure/logger/logger.js';
import { RecordingScope, RecordingStopReason } from '../../domain/recording/recording-session-state.js';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { SettingsRepository } from '../ports/settings-repository.js';
import type { FileDeletionRequester, FileDeletionTarget } from '../ports/file-deletion-requester.js';
import {
  failRecording,
  markRecordingFileAvailable,
  markRecordingFileRejected,
  stopRecordingBySystem,
  warnRecordingLimit,
  type RecordingLifecycleDeps,
} from './recording-lifecycle.js';

export interface RecordingSweepDeps extends RecordingLifecycleDeps {
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve' | 'platform'>;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
}

export interface RecordingSweepReport {
  readonly warned: number;
  readonly stoppedByLimit: number;
  readonly ready: number;
  readonly failed: number;
}

/** Estados de CloudStorage que significan "el archivo nunca estara disponible". */
const REJECTED_FILE_STATUSES = new Set(['Infected', 'Quarantined', 'ScanFailed', 'BlockedByPolicy', 'Rejected', 'Deleted']);
/** Un archivo recien subido tarda en registrarse/escanearse: no se consulta antes de este margen. */
const AVAILABILITY_CHECK_GRACE_MS = 60_000;

/**
 * F7: barrido de grabaciones (lock Redis afuera). (1) aviso y corte por limite (T-35); (2) respaldo de
 * disponibilidad cuando el evento de CloudStorage no llega (cuota o politica descartan en silencio);
 * (3) composiciones que el recorder nunca devolvio. Los plazos salen de la configuracion efectiva.
 */
export async function runRecordingSweep(now: Date, deps: RecordingSweepDeps): Promise<RecordingSweepReport> {
  let warned = 0;
  let stoppedByLimit = 0;
  let ready = 0;
  let failed = 0;

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

  for (const session of await deps.recordingSessions.listComposingStoppedBefore(new Date(now.getTime() - availabilityTimeoutMs))) {
    await failRecording(session, 'CompositionTimeout', deps, `recording:${session.id}`);
    failed += 1;
  }

  return { warned, stoppedByLimit, ready, failed };
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
  readonly settings: Pick<SettingsRepository, 'listPurgeEnabled'>;
  readonly fileDeletion: FileDeletionRequester;
}

/** Lote por vuelta: la retencion no tiene prisa y no debe competir con las reuniones. */
const RETENTION_BATCH = 50;

/**
 * F7 (T-37): (1) pistas de audio vencidas (`recordingTracksRetentionDays`, global) para todas las
 * oficinas; (2) grabaciones y transcripts vencidos solo en oficinas con `PurgeEnabled`. Borra por
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
  for (const tenant of await deps.settings.listPurgeEnabled()) {
    const effective = await deps.effectiveSettings.resolve(tenant.tenantId);
    if (!effective.recording.purgeEnabled) continue;
    const cutoff = new Date(now.getTime() - effective.recording.retentionDays * 86_400_000);
    for (const session of await deps.recordingSessions.listReadyToPurge(tenant.tenantId, cutoff, RETENTION_BATCH)) {
      const s = session.toSnapshot();
      const files: FileDeletionTarget[] = [];
      if (s.recordingFileId) files.push({ fileId: s.recordingFileId, ownerType: 'Communication', ownerId: s.scopeId, folderType: 'Recordings' });
      const transcript = await deps.transcripts?.findByScope(s.tenantId, s.scope, s.scopeId);
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
          meeting.clearRecordingFiles();
          await deps.meetings.save(meeting);
        }
      } else {
        const call = await deps.calls.findById(s.tenantId, s.scopeId);
        if (call) {
          call.clearRecordingFiles();
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
