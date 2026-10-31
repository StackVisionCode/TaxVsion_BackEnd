import type {
  RecordingSession as PrismaRecordingSession,
  RecordingConsentEvent as PrismaRecordingConsentEvent,
} from '@prisma/client';
import {
  RecordingSession,
  type RecordingSessionSnapshot,
  type RecordingTracksManifest,
} from '../../domain/recording/recording-session.js';
import {
  RecordingConsentEntry,
  type RecordingConsentEntrySnapshot,
} from '../../domain/recording/recording-consent-entry.js';
import {
  isRecordingCaptureMode,
  isRecordingScope,
  isRecordingSessionState,
  isRecordingStopReason,
} from '../../domain/recording/recording-session-state.js';
import { isRecordingConsentEntryStatus } from '../../domain/recording/recording-consent.js';

export function toDomainRecordingSession(row: PrismaRecordingSession): RecordingSession {
  if (!isRecordingScope(row.Scope)) throw new Error(`Corrupted recording scope '${row.Scope}'`);
  if (!isRecordingSessionState(row.State)) throw new Error(`Corrupted recording session state '${row.State}'`);
  const snapshot: RecordingSessionSnapshot = {
    id: row.Id,
    tenantId: row.TenantId,
    scope: row.Scope,
    scopeId: row.ScopeId,
    state: row.State,
    requestedByUserId: row.RequestedByUserId,
    requestedAtUtc: row.RequestedAtUtc,
    startedAtUtc: row.StartedAtUtc,
    stoppedAtUtc: row.StoppedAtUtc,
    recordingFileId: row.RecordingFileId,
    durationSeconds: row.DurationSeconds,
    failureReason: row.FailureReason,
    captureMode: isRecordingCaptureMode(row.CaptureMode) ? row.CaptureMode : 'Client',
    maxDurationSeconds: row.MaxDurationSeconds,
    limitWarnedAtUtc: row.LimitWarnedAtUtc,
    stopReason: row.StopReason && isRecordingStopReason(row.StopReason) ? row.StopReason : null,
    composedFileId: row.ComposedFileId,
    composedSizeBytes: row.ComposedSizeBytes === null ? null : Number(row.ComposedSizeBytes),
    composedFormat: row.ComposedFormat,
    tracksManifest: parseTracksManifest(row.TracksManifestJson),
    composedAtUtc: row.ComposedAtUtc,
    readyAtUtc: row.ReadyAtUtc,
    tracksPurgedAtUtc: row.TracksPurgedAtUtc,
    purgedAtUtc: row.PurgedAtUtc,
  };
  return RecordingSession.rehydrate(snapshot);
}

export function toDomainRecordingConsentEntry(row: PrismaRecordingConsentEvent): RecordingConsentEntry {
  if (!isRecordingScope(row.Scope)) throw new Error(`Corrupted recording scope '${row.Scope}'`);
  if (!isRecordingConsentEntryStatus(row.Response)) {
    throw new Error(`Corrupted recording consent response '${row.Response}'`);
  }
  const snapshot: RecordingConsentEntrySnapshot = {
    id: row.Id,
    tenantId: row.TenantId,
    scope: row.Scope,
    scopeId: row.ScopeId,
    userId: row.UserId,
    response: row.Response,
    respondedAtUtc: row.RespondedAtUtc,
    recordedAtUtc: row.RecordedAtUtc,
  };
  return RecordingConsentEntry.rehydrate(snapshot);
}

/** Manifest guardado como JSON; uno corrupto se trata como ausente (no hay pistas que transcribir/purgar). */
export function parseTracksManifest(raw: string | null): RecordingTracksManifest | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<RecordingTracksManifest>;
    if (!value || !Array.isArray(value.audioTracks)) return null;
    return { audioTracks: value.audioTracks, videoTrackCount: typeof value.videoTrackCount === 'number' ? value.videoTrackCount : 0 };
  } catch {
    return null;
  }
}
