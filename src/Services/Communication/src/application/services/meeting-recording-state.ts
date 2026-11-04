import type { RecordingSession } from '../../domain/recording/recording-session.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRecordingStateChangedDto } from '../../contracts/socket/meeting-socket-events.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';

/** Estado de una grabacion tal como lo ve la sala (evento en vivo y snapshot al entrar). */
export function recordingStateOf(session: RecordingSession, reason?: string | null): Omit<MeetingRecordingStateChangedDto, 'meetingId'> {
  const s = session.toSnapshot();
  return {
    state: s.state,
    updatedAtUtc: new Date().toISOString(),
    captureMode: s.captureMode,
    startedAtUtc: s.startedAtUtc ? s.startedAtUtc.toISOString() : null,
    maxDurationSeconds: s.maxDurationSeconds,
    reason: reason ?? (s.state === 'Stopping' ? s.stopReason : s.state === 'Failed' ? s.failureReason : null),
    recordingId: s.id,
    requestedByUserId: s.requestedByUserId,
  };
}

/** MNT-01 (G-2): quien entra o vuelve a la sala sabe si se esta grabando sin esperar al proximo evento. */
export async function currentMeetingRecording(
  sessions: Pick<RecordingSessionRepository, 'findLatestByScope'> | undefined,
  tenantId: string,
  meetingId: string,
): Promise<MeetingRecordingStateChangedDto | null> {
  if (!sessions) return null;
  const latest = await sessions.findLatestByScope(tenantId, RecordingScope.Meeting, meetingId).catch(() => null);
  return latest ? { meetingId, ...recordingStateOf(latest) } : null;
}
