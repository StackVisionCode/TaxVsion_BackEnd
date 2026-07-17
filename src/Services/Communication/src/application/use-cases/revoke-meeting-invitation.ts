import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';

export interface RevokeMeetingInvitationCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly invitationId: string;
  readonly actorUserId: string;
}

export interface RevokeMeetingInvitationDeps {
  readonly meetings: MeetingRepository;
}

/** Host/Cohost. Revocar una invitation ya usada/expirada es un no-op valido (idempotente). */
export async function revokeMeetingInvitation(
  command: RevokeMeetingInvitationCommand,
  deps: RevokeMeetingInvitationDeps,
): Promise<Result<void>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can revoke invitations.'));

  const invitation = await deps.meetings.findInvitationById(command.tenantId, command.invitationId);
  if (!invitation || invitation.toSnapshot().meetingId !== command.meetingId) {
    return Result.fail(makeError('Meeting.Invitation.NotFound', 'Invitation not found.'));
  }

  invitation.revoke(new Date());
  await deps.meetings.saveInvitation(invitation);
  return Result.okVoid();
}
