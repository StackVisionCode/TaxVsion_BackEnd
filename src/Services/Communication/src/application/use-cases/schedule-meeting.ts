import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { Meeting } from '../../domain/meetings/meeting.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { PasscodeHasher } from '../ports/passcode-hasher.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type { MeetingMediaPolicyProvider } from '../ports/meeting-media-policy-provider.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
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
  /** Informativa (D-35), acotada por el máximo de la oficina; sin valor, el default de la oficina. */
  readonly scheduledDurationMinutes?: number;
  readonly recordingRequested?: boolean;
  /** null/ausente = automático (INV-T01 R-7). */
  readonly transcriptLanguage?: string | null;
  /** "Start a new meeting" (D-34): reunión de la que ésta es continuación. */
  readonly continuationOfMeetingId?: string | null;
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
  /** Umbral mesh/SFU y cupo de la oficina (global → plan → techo → oficina), fuera del dominio. */
  readonly meetingMediaPolicy: MeetingMediaPolicyProvider;
  /** Duración por defecto y máxima de la oficina (resolver). */
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
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
  const mediaPolicy = await deps.meetingMediaPolicy.get(command.tenantId);
  const effective = await deps.effectiveSettings.resolve(command.tenantId);
  const durationPolicy = {
    defaultDurationMinutes: effective.meeting.defaultDurationMinutes,
    maxDurationMinutes: effective.meeting.maxDurationMinutes,
  };
  // El cupo efectivo de la oficina es a la vez el default y el techo (T-40): nadie lo supera a mano.
  if (command.maxParticipants !== undefined && command.maxParticipants > mediaPolicy.defaultMaxParticipants) {
    return Result.fail(
      makeError('Plan.MeetingSizeExceeded', `Your plan allows up to ${mediaPolicy.defaultMaxParticipants} participants.`),
    );
  }

  const scheduleResult = Meeting.schedule({
    tenantId: command.tenantId,
    title: command.title,
    description: command.description ?? null,
    host: command.host,
    ...(command.maxParticipants !== undefined ? { maxParticipants: command.maxParticipants } : {}),
    requireWaitingRoom: command.requireWaitingRoom ?? true,
    passcodeHash,
    scheduledForUtc: command.scheduledForUtc ? new Date(command.scheduledForUtc) : null,
    ...(command.scheduledDurationMinutes !== undefined ? { scheduledDurationMinutes: command.scheduledDurationMinutes } : {}),
    recordingRequested: command.recordingRequested ?? false,
    transcriptLanguage: command.transcriptLanguage ?? null,
    mediaPolicy,
    durationPolicy,
    continuationOfMeetingId: command.continuationOfMeetingId ?? null,
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
