import { Result, makeError } from '../../domain/shared/result.js';
import { isStaffActor } from '../../domain/shared/permissions.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { TranscriptArtifactRepository } from '../ports/transcript-artifact-repository.js';
import type { CloudStorageDownloadClient } from '../ports/cloudstorage-download-client.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import {
  canSeeRecording,
  participatedInRecording,
  type RecordingSubject,
} from '../services/recording-visibility.js';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import { findRecordingTranscript } from '../services/recording-transcript-lookup.js';

export interface PresignedUrlDto {
  readonly url: string;
  readonly expiresAtUtc: string;
}

/** Contrato 9.6: lo que la ficha muestra de una grabacion (y su transcript). */
export interface RecordingItemDto {
  readonly recordingId: string;
  /** D-71: numero de la grabacion dentro del meeting/call. */
  readonly sequence: number;
  readonly state: string;
  readonly captureMode: string;
  readonly startedAtUtc: string | null;
  readonly stoppedAtUtc: string | null;
  readonly durationSeconds: number | null;
  readonly maxDurationSeconds: number | null;
  readonly stopReason: string | null;
  readonly format: string | null;
  readonly sizeBytes: number | null;
  readonly failureReason: string | null;
  readonly playback: PresignedUrlDto | null;
  readonly transcript: {
    readonly state: 'Processing' | 'Ready' | 'Failed';
    readonly language: string | null;
    readonly speakerCount: number | null;
    readonly wordCount: number | null;
    readonly txt: PresignedUrlDto | null;
    readonly json: PresignedUrlDto | null;
    readonly failureReason: string | null;
  } | null;
}

/** La ultima grabacion (campos de siempre) + todas las del meeting/call, de la primera a la ultima (D-71). */
export interface RecordingDetailsDto extends RecordingItemDto {
  readonly recordings: readonly RecordingItemDto[];
}

export interface GetRecordingDetailsDeps {
  readonly meetings: MeetingRepository;
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly transcripts: TranscriptArtifactRepository;
  readonly cloudStorageDownload: CloudStorageDownloadClient;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly customerAssignments: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'>;
  /** Visibilidad por asignacion activa en el despliegue (mismo criterio que el historial de llamadas). */
  readonly assignmentVisibilityEnabled: boolean;
}

export interface RecordingViewer {
  readonly tenantId: string;
  readonly userId: string;
  readonly actorType: string;
  /** Staff con el permiso de grabar del scope; solo cuenta `call.record` en llamadas con cliente (E-14). */
  readonly holdsRecordPermission: boolean;
}

/**
 * F7 (contrato 9.6). Personal: la regla de la biblioteca (E-1/E-14), quien participo y, en una llamada con
 * cliente, quien tiene el permiso de grabar y ve a ese cliente. El cliente del portal, si participo y la
 * oficina las hace visibles (D-29). Las URLs presignadas solo se emiten con la grabacion Ready.
 */
export async function getRecordingDetails(
  input: { viewer: RecordingViewer; scope: RecordingScope; scopeId: string },
  deps: GetRecordingDetailsDeps,
): Promise<Result<RecordingDetailsDto>> {
  const { viewer } = input;
  const notFound = makeError(`${input.scope}.Recording.NotFound`, 'Recording not found.');
  const forbidden = makeError(
    `${input.scope}.Recording.Forbidden`,
    "You don't have access to this recording.",
  );

  const subject = await subjectOf(input.scope, input.scopeId, viewer.tenantId, deps);
  if (!subject) return Result.fail(notFound);
  if (!isStaffActor(viewer.actorType)) {
    // Portal (y cualquier actor que no sea personal): solo quien participó, como siempre.
    if (!participatedInRecording(subject, viewer.userId)) return Result.fail(forbidden);
  } else {
    // E-14: la regla de la biblioteca; quien no la ve recibe lo mismo que para una grabación inexistente.
    const holdsCallRecordPermission = subject.scope === 'Call' && viewer.holdsRecordPermission;
    if (!(await canSeeRecording({ ...viewer, holdsCallRecordPermission }, subject, deps)))
      return Result.fail(notFound);
  }

  if (viewer.actorType === 'CustomerPortal') {
    const effective = await deps.effectiveSettings.resolve(viewer.tenantId);
    if (!effective.recording.visibleInPortal) return Result.fail(forbidden);
  }

  const sessions = (
    await deps.recordingSessions.listByScope(viewer.tenantId, input.scope, input.scopeId)
  ).filter((session) => !session.toSnapshot().purgedAtUtc);
  const latest = sessions.at(-1);
  if (!latest) return Result.fail(notFound);
  const recordings = await Promise.all(
    sessions.map((session) => toRecordingItem(session, viewer.tenantId, deps)),
  );
  return Result.ok({ ...recordings[recordings.length - 1]!, recordings });
}

async function subjectOf(
  scope: RecordingScope,
  scopeId: string,
  tenantId: string,
  deps: Pick<GetRecordingDetailsDeps, 'meetings' | 'calls'>,
): Promise<RecordingSubject | null> {
  if (scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(tenantId, scopeId);
    return meeting ? { scope: 'Meeting', meeting } : null;
  }
  const call = await deps.calls.findById(tenantId, scopeId);
  return call ? { scope: 'Call', call } : null;
}

async function toRecordingItem(
  session: RecordingSession,
  tenantId: string,
  deps: GetRecordingDetailsDeps,
): Promise<RecordingItemDto> {
  const s = session.toSnapshot();
  const ready = s.state === 'Ready' && s.recordingFileId !== null;
  const playback = ready ? await presign(deps, tenantId, s.recordingFileId) : null;

  const artifact = await findRecordingTranscript(deps.transcripts, session);
  const t = artifact?.toSnapshot();
  const transcript = t
    ? {
        state: t.state,
        language: t.language,
        speakerCount: t.speakerCount,
        wordCount: t.wordCount,
        txt: t.state === 'Ready' ? await presign(deps, tenantId, t.txtFileId) : null,
        json: t.state === 'Ready' ? await presign(deps, tenantId, t.jsonFileId) : null,
        failureReason: t.failureReason,
      }
    : null;

  return {
    recordingId: s.id,
    sequence: s.sequence,
    state: s.state,
    captureMode: s.captureMode,
    startedAtUtc: s.startedAtUtc ? s.startedAtUtc.toISOString() : null,
    stoppedAtUtc: s.stoppedAtUtc ? s.stoppedAtUtc.toISOString() : null,
    durationSeconds: s.durationSeconds,
    maxDurationSeconds: s.maxDurationSeconds,
    stopReason: s.stopReason,
    format: s.composedFormat,
    sizeBytes: s.composedSizeBytes,
    failureReason: s.state === 'Failed' ? s.failureReason : null,
    playback,
    transcript,
  };
}

/** URL de descarga presignada (Communication autoriza; CloudStorage firma como servicio). */
async function presign(
  deps: GetRecordingDetailsDeps,
  tenantId: string,
  fileId: string | null,
): Promise<PresignedUrlDto | null> {
  if (!fileId) return null;
  const url = await deps.cloudStorageDownload.getDownloadUrl(tenantId, fileId);
  return url ? { url: url.downloadUrl, expiresAtUtc: url.expiresAtUtc } : null;
}
