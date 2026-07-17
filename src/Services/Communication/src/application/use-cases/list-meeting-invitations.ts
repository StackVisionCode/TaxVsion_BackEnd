import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingInviteeKind } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';

export interface ListMeetingInvitationsCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
}

export interface MeetingInvitationListItem {
  readonly id: string;
  readonly inviteeKind: MeetingInviteeKind;
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly inviteeName: string | null;
  readonly expiresAt: string;
  readonly usedAt: string | null;
  readonly revokedAt: string | null;
  readonly createdAt: string;
}

export interface ListMeetingInvitationsResult {
  readonly invitations: readonly MeetingInvitationListItem[];
}

export interface ListMeetingInvitationsDeps {
  readonly meetings: MeetingRepository;
}

/** Host/Cohost — la lista nunca incluye el token ni su hash, solo metadata de estado. */
export async function listMeetingInvitations(
  command: ListMeetingInvitationsCommand,
  deps: ListMeetingInvitationsDeps,
): Promise<Result<ListMeetingInvitationsResult>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can list invitations.'));

  const rows = await deps.meetings.listInvitationsByMeeting(command.tenantId, command.meetingId);
  const invitations = rows.map((invitation) => {
    const s = invitation.toSnapshot();
    return {
      id: s.id,
      inviteeKind: s.inviteeKind,
      inviteeEmail: s.inviteeEmail,
      inviteeUserId: s.inviteeUserId,
      inviteeName: s.inviteeName,
      expiresAt: s.expiresAtUtc.toISOString(),
      usedAt: s.usedAtUtc ? s.usedAtUtc.toISOString() : null,
      revokedAt: s.revokedAtUtc ? s.revokedAtUtc.toISOString() : null,
      createdAt: s.createdAtUtc.toISOString(),
    };
  });

  return Result.ok({ invitations });
}
