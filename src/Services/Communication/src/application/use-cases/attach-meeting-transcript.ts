import type { MeetingRepository } from '../ports/meeting-repository.js';
import { Result, makeError } from '../../domain/shared/result.js';

/** Ver docblock de attach-call-transcript.ts — mismo flujo, para meetings. */
export interface AttachMeetingTranscriptCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly transcriptFileId: string;
}

export interface AttachMeetingTranscriptResult {
  readonly meetingId: string;
  readonly transcriptFileId: string;
}

export async function attachMeetingTranscript(
  cmd: AttachMeetingTranscriptCommand,
  deps: { meetings: MeetingRepository },
): Promise<Result<AttachMeetingTranscriptResult>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const result = meeting.attachTranscript(cmd.transcriptFileId);
  if (!result.isSuccess) return Result.fail(result.error);

  await deps.meetings.save(meeting);
  return Result.ok({ meetingId: meeting.id, transcriptFileId: cmd.transcriptFileId });
}
