import type { Call as PrismaCall, CallParticipant as PrismaParticipant } from '@prisma/client';
import { Call, type CallSnapshot } from '../../domain/calls/call.js';
import { isCallKind } from '../../domain/calls/call-kind.js';
import { isCallStatus } from '../../domain/calls/call-status.js';
import type { CallParticipantSnapshot } from '../../domain/calls/call-participant.js';
import { isConnectionQuality } from '../../domain/calls/media-status.js';

export function toDomainCallParticipant(row: PrismaParticipant): CallParticipantSnapshot {
  return {
    id: row.Id,
    callId: row.CallId,
    tenantId: row.TenantId,
    userId: row.UserId,
    displayName: row.DisplayName,
    role: row.Role === 'Caller' ? 'Caller' : 'Callee',
    joinOrder: row.JoinOrder,
    joinedAtUtc: row.JoinedAtUtc,
    leftAtUtc: row.LeftAtUtc,
    audioEnabled: row.AudioEnabled,
    videoEnabled: row.VideoEnabled,
    screenSharing: row.ScreenSharing,
    connectionQuality: isConnectionQuality(row.ConnectionQuality) ? row.ConnectionQuality : 'Unknown',
  };
}

export function toDomainCall(row: PrismaCall, participants: PrismaParticipant[]): Call {
  if (!isCallKind(row.Kind)) {
    throw new Error(`Corrupted Call row: unknown Kind '${row.Kind}' (id=${row.Id})`);
  }
  if (!isCallStatus(row.Status)) {
    throw new Error(`Corrupted Call row: unknown Status '${row.Status}' (id=${row.Id})`);
  }
  const snapshot: CallSnapshot = {
    id: row.Id,
    tenantId: row.TenantId,
    kind: row.Kind,
    status: row.Status,
    callerUserId: row.CallerUserId,
    calleeUserId: row.CalleeUserId,
    conversationId: row.ConversationId,
    ringingAtUtc: row.RingingAtUtc,
    acceptedAtUtc: row.AcceptedAtUtc,
    startedAtUtc: row.StartedAtUtc,
    endedAtUtc: row.EndedAtUtc,
    endReason:
      row.EndReason === 'Hangup' ||
      row.EndReason === 'Missed' ||
      row.EndReason === 'Rejected' ||
      row.EndReason === 'Cancelled' ||
      row.EndReason === 'IceFailed'
        ? row.EndReason
        : null,
    durationSeconds: row.DurationSeconds,
    recordingRequested: row.RecordingRequested,
    recordingFileId: row.RecordingFileId,
    createdAtUtc: row.CreatedAtUtc,
    updatedAtUtc: row.UpdatedAtUtc,
    participants: participants.map(toDomainCallParticipant),
  };
  return Call.rehydrate(snapshot);
}
