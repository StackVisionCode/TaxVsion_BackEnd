import { logger } from '../../infrastructure/logger/logger.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { TranscriptArtifactRepository } from '../ports/transcript-artifact-repository.js';

export interface TranscriptFileStatusDeps {
  readonly transcripts: Pick<TranscriptArtifactRepository, 'save' | 'findByFileId'>;
  readonly meetings: Pick<MeetingRepository, 'findById' | 'save'>;
  readonly calls: Pick<CallRepository, 'findById' | 'save'>;
}

export interface TranscriptFileCommand {
  readonly tenantId: string;
  readonly fileId: string;
}

/** MNT-01 (E-292): CloudStorage confirmo un archivo del transcript; el barrido ya no lo revisa. */
export async function markTranscriptFileAvailable(cmd: TranscriptFileCommand, deps: TranscriptFileStatusDeps): Promise<boolean> {
  const artifact = await deps.transcripts.findByFileId?.(cmd.tenantId, cmd.fileId);
  if (!artifact || artifact.toSnapshot().txtFileId !== cmd.fileId) return false;
  artifact.confirmFiles();
  await deps.transcripts.save(artifact);
  return true;
}

/**
 * MNT-01 (E-292): CloudStorage rechazo (o nunca registro) un archivo del transcript. Sin el .txt el
 * transcript queda Failed y la reunion o llamada deja de apuntarlo: antes el boton "Transcript" abria
 * un archivo inexistente ("The file was not found").
 */
export async function releaseRejectedTranscriptFile(
  cmd: TranscriptFileCommand & { readonly reason: string },
  deps: TranscriptFileStatusDeps,
): Promise<boolean> {
  const artifact = await deps.transcripts.findByFileId?.(cmd.tenantId, cmd.fileId);
  if (!artifact) return false;
  const released = artifact.releaseRejectedFile(cmd.fileId, cmd.reason);
  if (!released) return false;
  await deps.transcripts.save(artifact);
  const a = artifact.toSnapshot();
  logger.warn({ tenantId: cmd.tenantId, fileId: cmd.fileId, scope: a.scope, scopeId: a.scopeId, reason: cmd.reason, released }, 'transcript file rejected by storage');
  if (released !== 'txt') return true;

  const files = { recordingFileId: null, transcriptFileId: cmd.fileId };
  if (a.scope === 'Meeting') {
    const meeting = await deps.meetings.findById(cmd.tenantId, a.scopeId);
    if (meeting && meeting.toSnapshot().transcriptFileId === cmd.fileId) {
      meeting.clearRecordingFiles(files);
      await deps.meetings.save(meeting);
    }
  } else if (a.scope === 'Call') {
    const call = await deps.calls.findById(cmd.tenantId, a.scopeId);
    if (call && call.toSnapshot().transcriptFileId === cmd.fileId) {
      call.clearRecordingFiles(files);
      await deps.calls.save(call);
    }
  }
  return true;
}
