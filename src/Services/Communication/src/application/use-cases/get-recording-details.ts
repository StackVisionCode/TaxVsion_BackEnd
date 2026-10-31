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
import { seesAllCustomers } from './customer-visibility.js';

export interface PresignedUrlDto {
  readonly url: string;
  readonly expiresAtUtc: string;
}

/** Contrato 9.6: lo que la ficha muestra de una grabacion (y su transcript). */
export interface RecordingDetailsDto {
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
  /** Staff con el permiso de grabar del scope (lo resuelve la ruta con la proyeccion de permisos). */
  readonly holdsRecordPermission: boolean;
}

/**
 * F7 (contrato 9.6). Quien participo ve la grabacion; el staff que no participo necesita el permiso de
 * grabar (y, en llamadas con cliente, verlo segun la asignacion); el cliente del portal solo si la
 * oficina las hace visibles (D-29). Las URLs presignadas solo se emiten con la grabacion Ready.
 */
export async function getRecordingDetails(
  input: { viewer: RecordingViewer; scope: RecordingScope; scopeId: string },
  deps: GetRecordingDetailsDeps,
): Promise<Result<RecordingDetailsDto>> {
  const { viewer } = input;
  const notFound = makeError(`${input.scope}.Recording.NotFound`, 'Recording not found.');
  const forbidden = makeError(`${input.scope}.Recording.Forbidden`, "You don't have access to this recording.");

  if (input.scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(viewer.tenantId, input.scopeId);
    if (!meeting) return Result.fail(notFound);
    const participated = meeting.hostUserId === viewer.userId || meeting.isKnownParticipant(viewer.userId);
    if (!participated && !(isStaffActor(viewer.actorType) && viewer.holdsRecordPermission)) {
      return Result.fail(forbidden);
    }
  } else {
    const call = await deps.calls.findById(viewer.tenantId, input.scopeId);
    if (!call) return Result.fail(notFound);
    if (!call.isParticipant(viewer.userId)) {
      if (!isStaffActor(viewer.actorType) || !viewer.holdsRecordPermission) return Result.fail(forbidden);
      const customerIds = call.toSnapshot().participants.map((p) => p.customerId).filter((id): id is string => !!id);
      if (customerIds.length > 0 && deps.assignmentVisibilityEnabled && !(await seesAllCustomers(viewer, deps.userPermissions))) {
        const assigned = await Promise.all(customerIds.map((id) => deps.customerAssignments.isAssigned(viewer.tenantId, id, viewer.userId)));
        if (!assigned.some(Boolean)) return Result.fail(forbidden);
      }
    }
  }

  if (viewer.actorType === 'CustomerPortal') {
    const effective = await deps.effectiveSettings.resolve(viewer.tenantId);
    if (!effective.recording.visibleInPortal) return Result.fail(forbidden);
  }

  const session = await deps.recordingSessions.findByScope(viewer.tenantId, input.scope, input.scopeId);
  if (!session || session.toSnapshot().purgedAtUtc) return Result.fail(notFound);
  const s = session.toSnapshot();
  const ready = s.state === 'Ready' && s.recordingFileId !== null;
  const playback = ready ? await presign(deps, viewer.tenantId, s.recordingFileId) : null;

  const artifact = await deps.transcripts.findByScope(viewer.tenantId, input.scope, input.scopeId);
  const t = artifact?.toSnapshot();
  const transcript = t
    ? {
        state: t.state,
        language: t.language,
        speakerCount: t.speakerCount,
        wordCount: t.wordCount,
        txt: t.state === 'Ready' ? await presign(deps, viewer.tenantId, t.txtFileId) : null,
        json: t.state === 'Ready' ? await presign(deps, viewer.tenantId, t.jsonFileId) : null,
        failureReason: t.failureReason,
      }
    : null;

  return Result.ok({
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
  });
}

/** URL de descarga presignada (Communication autoriza; CloudStorage firma como servicio). */
async function presign(deps: GetRecordingDetailsDeps, tenantId: string, fileId: string | null): Promise<PresignedUrlDto | null> {
  if (!fileId) return null;
  const url = await deps.cloudStorageDownload.getDownloadUrl(tenantId, fileId);
  return url ? { url: url.downloadUrl, expiresAtUtc: url.expiresAtUtc } : null;
}
