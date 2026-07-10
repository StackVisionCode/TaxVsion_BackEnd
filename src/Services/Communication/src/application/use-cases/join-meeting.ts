import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { PasscodeHasher } from '../ports/passcode-hasher.js';
import type { MeetingSnapshotDto } from '../../contracts/socket/meeting-socket-events.js';
import { participantSnapshotToDto } from './meeting-mappers.js';

export interface JoinMeetingCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly user: { userId: string; displayName: string };
  readonly passcode?: string;
  readonly invitationToken?: string;
}

export interface JoinMeetingResult {
  readonly snapshot: MeetingSnapshotDto;
  readonly requiresAdmission: boolean;
}

export interface JoinMeetingDeps {
  readonly meetings: MeetingRepository;
  readonly passcodes: PasscodeHasher;
}

export async function joinMeeting(
  command: JoinMeetingCommand,
  deps: JoinMeetingDeps,
): Promise<Result<JoinMeetingResult>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  let invitationValid = false;
  if (command.invitationToken) {
    const hash = MeetingInvitation.hash(command.invitationToken);
    const invitation = await deps.meetings.findInvitationByHash(hash);
    if (invitation) {
      const validation = invitation.validateForUse({
        plainToken: command.invitationToken,
        now: new Date(),
      });
      if (validation.isSuccess) {
        invitationValid = true;
        invitation.markUsed(new Date());
        await deps.meetings.saveInvitation(invitation);
      }
    }
  }

  let passcodeMatch: boolean | null = null;
  if (meeting.requiresPasscode) {
    passcodeMatch = command.passcode
      ? await deps.passcodes.verify(meeting.passcodeHash!, command.passcode)
      : false;
  }

  const joinResult = meeting.requestJoin({
    userId: command.user.userId,
    displayName: command.user.displayName,
    hasValidInvitation: invitationValid,
    passcodeMatch,
  });
  if (!joinResult.isSuccess) return Result.fail(joinResult.error);

  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();
  const yourRole = joinResult.value.role;

  const dto: MeetingSnapshotDto = {
    meetingId: snapshot.id,
    status: snapshot.status,
    strategy: snapshot.strategy,
    hostUserId: snapshot.hostUserId,
    isLocked: snapshot.isLocked,
    participants: snapshot.participants.map(participantSnapshotToDto),
    yourRole,
    sequence: 0,
  };

  return Result.ok({ snapshot: dto, requiresAdmission: joinResult.value.requiresAdmission });
}
