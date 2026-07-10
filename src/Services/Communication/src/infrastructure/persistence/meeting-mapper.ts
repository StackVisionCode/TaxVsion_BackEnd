import type {
  Meeting as PrismaMeeting,
  MeetingParticipant as PrismaParticipant,
  MeetingInvitation as PrismaInvitation,
} from '@prisma/client';
import { Meeting, type MeetingSnapshot } from '../../domain/meetings/meeting.js';
import {
  MeetingInvitation,
  type MeetingInvitationSnapshot,
} from '../../domain/meetings/meeting-invitation.js';
import type { MeetingParticipantSnapshot } from '../../domain/meetings/meeting-participant.js';
import {
  isMeetingRole,
  isMeetingStatus,
  isMeetingStrategy,
  isParticipantStatus,
} from '../../domain/meetings/meeting-enums.js';
import { isConnectionQuality } from '../../domain/calls/media-status.js';

export function toDomainMeetingParticipant(row: PrismaParticipant): MeetingParticipantSnapshot {
  if (!isMeetingRole(row.Role)) throw new Error(`Corrupted meeting role '${row.Role}'`);
  if (!isParticipantStatus(row.Status)) throw new Error(`Corrupted participant status '${row.Status}'`);
  return {
    id: row.Id,
    meetingId: row.MeetingId,
    tenantId: row.TenantId,
    userId: row.UserId,
    displayName: row.DisplayName,
    role: row.Role,
    status: row.Status,
    joinOrder: row.JoinOrder,
    requestedAtUtc: row.RequestedAtUtc,
    admittedAtUtc: row.AdmittedAtUtc,
    joinedAtUtc: row.JoinedAtUtc,
    leftAtUtc: row.LeftAtUtc,
    audioEnabled: row.AudioEnabled,
    videoEnabled: row.VideoEnabled,
    screenSharing: row.ScreenSharing,
    handRaised: row.HandRaised,
    connectionQuality: isConnectionQuality(row.ConnectionQuality) ? row.ConnectionQuality : 'Unknown',
  };
}

export function toDomainMeeting(row: PrismaMeeting, participants: PrismaParticipant[]): Meeting {
  if (!isMeetingStatus(row.Status)) throw new Error(`Corrupted meeting status '${row.Status}'`);
  if (!isMeetingStrategy(row.Strategy)) throw new Error(`Corrupted meeting strategy '${row.Strategy}'`);
  const snapshot: MeetingSnapshot = {
    id: row.Id,
    tenantId: row.TenantId,
    title: row.Title,
    description: row.Description,
    status: row.Status,
    shortCode: row.ShortCode,
    passcodeHash: row.PasscodeHash,
    requireWaitingRoom: row.RequireWaitingRoom,
    isLocked: row.IsLocked,
    maxParticipants: row.MaxParticipants,
    strategy: row.Strategy,
    recordingRequested: row.RecordingRequested,
    recordingFileId: row.RecordingFileId,
    hostUserId: row.HostUserId,
    scheduledForUtc: row.ScheduledForUtc,
    startedAtUtc: row.StartedAtUtc,
    endedAtUtc: row.EndedAtUtc,
    durationSeconds: row.DurationSeconds,
    createdByUserId: row.CreatedByUserId,
    createdAtUtc: row.CreatedAtUtc,
    updatedAtUtc: row.UpdatedAtUtc,
    participants: participants.map(toDomainMeetingParticipant),
  };
  return Meeting.rehydrate(snapshot);
}

export function toDomainMeetingInvitation(row: PrismaInvitation): MeetingInvitation {
  const snapshot: MeetingInvitationSnapshot = {
    id: row.Id,
    meetingId: row.MeetingId,
    tenantId: row.TenantId,
    inviteeEmail: row.InviteeEmail,
    inviteeUserId: row.InviteeUserId,
    tokenHash: row.TokenHash,
    expiresAtUtc: row.ExpiresAtUtc,
    usedAtUtc: row.UsedAtUtc,
    revokedAtUtc: row.RevokedAtUtc,
    createdAtUtc: row.CreatedAtUtc,
  };
  return MeetingInvitation.rehydrate(snapshot);
}
