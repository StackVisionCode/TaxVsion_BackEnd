import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';

export interface SetMeetingTranscriptLanguageCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly byUserId: string;
  /** `en`, `es`, `auto`… o null = automático (cada participante / la oficina). */
  readonly language: string | null;
}

/** Host o cohost fijan el idioma del transcript de la reunión (INV-T01 R-7). */
export async function setMeetingTranscriptLanguage(
  cmd: SetMeetingTranscriptLanguageCommand,
  deps: { readonly meetings: MeetingRepository },
): Promise<Result<{ transcriptLanguage: string | null }>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const changed = meeting.setTranscriptLanguage({ byUserId: cmd.byUserId, language: cmd.language });
  if (!changed.isSuccess) return Result.fail(changed.error);
  await deps.meetings.save(meeting);
  return Result.ok({ transcriptLanguage: meeting.toSnapshot().transcriptLanguage });
}
