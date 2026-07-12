import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Meeting } from '../../domain/meetings/meeting.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { PasscodeHasher } from '../ports/passcode-hasher.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import { MeetingEventTypes, type MeetingScheduledEvent } from '../../contracts/events/meeting-events.js';

export interface ScheduleMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly host: { userId: string; displayName: string };
  readonly title: string;
  readonly description?: string | null;
  readonly maxParticipants?: number;
  readonly passcodePlain?: string | null;
  readonly requireWaitingRoom?: boolean;
  readonly scheduledForUtc?: string | null;
  readonly recordingRequested?: boolean;
}

export interface ScheduleMeetingResult {
  readonly meetingId: string;
  readonly shortCode: string;
  readonly requiresPasscode: boolean;
}

export interface ScheduleMeetingDeps {
  readonly meetings: MeetingRepository;
  readonly passcodes: PasscodeHasher;
  readonly publisher: IntegrationEventPublisher;
  readonly settings: TenantSettingsProvider;
}

export async function scheduleMeeting(
  command: ScheduleMeetingCommand,
  deps: ScheduleMeetingDeps,
): Promise<Result<ScheduleMeetingResult>> {
  const settings = await deps.settings.get(command.tenantId);
  if (!settings.chatEnabled) {
    return Result.fail(makeError('Meeting.Disabled', 'Communication is disabled for this tenant.'));
  }
  const passcodeHash = command.passcodePlain
    ? await deps.passcodes.hash(command.passcodePlain)
    : null;

  const scheduleResult = Meeting.schedule({
    tenantId: command.tenantId,
    title: command.title,
    description: command.description ?? null,
    host: command.host,
    maxParticipants: command.maxParticipants ?? 4,
    requireWaitingRoom: command.requireWaitingRoom ?? true,
    passcodeHash,
    scheduledForUtc: command.scheduledForUtc ? new Date(command.scheduledForUtc) : null,
    recordingRequested: command.recordingRequested ?? false,
  });
  if (!scheduleResult.isSuccess) return Result.fail(scheduleResult.error);

  const meeting = scheduleResult.value;
  await deps.meetings.save(meeting);
  const snapshot = meeting.toSnapshot();

  const event: MeetingScheduledEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Scheduled,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: snapshot.createdAtUtc.toISOString(),
    meetingId: snapshot.id,
    title: snapshot.title,
    hostUserId: snapshot.hostUserId,
    scheduledForUtc: snapshot.scheduledForUtc ? snapshot.scheduledForUtc.toISOString() : null,
    shortCode: snapshot.shortCode,
  };
  await deps.publisher.enqueue(event);

  return Result.ok({
    meetingId: snapshot.id,
    shortCode: snapshot.shortCode,
    requiresPasscode: meeting.requiresPasscode,
  });
}
