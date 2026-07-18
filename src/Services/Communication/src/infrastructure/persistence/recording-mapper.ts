import type {
  RecordingSession as PrismaRecordingSession,
  RecordingConsentEvent as PrismaRecordingConsentEvent,
} from '@prisma/client';
import { RecordingSession, type RecordingSessionSnapshot } from '../../domain/recording/recording-session.js';
import {
  RecordingConsentEntry,
  type RecordingConsentEntrySnapshot,
} from '../../domain/recording/recording-consent-entry.js';
import { isRecordingSessionState, isRecordingScope } from '../../domain/recording/recording-session-state.js';
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
