import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';

export interface ResolveMeetingByCodeCommand {
  readonly shortCode: string;
}

export interface ResolveMeetingByCodeResult {
  readonly title: string;
  readonly host: string;
  readonly requiresPasscode: boolean;
  readonly requiresInvitation: boolean;
}

export interface ResolveMeetingByCodeDeps {
  readonly meetings: MeetingRepository;
}

/** HTTP publico (GET by-code) — deliberadamente NO expone la lista de participantes. */
export async function resolveMeetingByCode(
  command: ResolveMeetingByCodeCommand,
  deps: ResolveMeetingByCodeDeps,
): Promise<Result<ResolveMeetingByCodeResult>> {
  const meeting = await deps.meetings.findByShortCodeAnyTenant(command.shortCode);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const snap = meeting.toSnapshot();
  const hostParticipant = snap.participants.find((p) => p.userId === snap.hostUserId);

  return Result.ok({
    title: snap.title,
    host: hostParticipant?.displayName ?? 'Host',
    requiresPasscode: meeting.requiresPasscode,
    requiresInvitation: snap.isLocked,
  });
}
