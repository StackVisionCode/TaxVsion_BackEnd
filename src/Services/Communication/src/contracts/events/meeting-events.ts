import type { IntegrationEvent } from './integration-event.js';

export const MeetingEventTypes = {
  Scheduled: 'communication.meeting.scheduled.v1',
  Started: 'communication.meeting.started.v1',
  Ended: 'communication.meeting.ended.v1',
  InvitationRequested: 'communication.meeting.invitation_requested.v1',
  RecordingReady: 'communication.meeting.recording_ready.v1',
} as const;

export interface MeetingScheduledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.scheduled.v1';
  readonly meetingId: string;
  readonly title: string;
  readonly hostUserId: string;
  readonly scheduledForUtc: string | null;
  readonly shortCode: string;
}

export interface MeetingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.started.v1';
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly startedAtUtc: string;
}

export interface MeetingEndedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.ended.v1';
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly endedAtUtc: string;
  readonly durationSeconds: number;
  readonly recordingFileId: string | null;
}

export interface MeetingInvitationRequestedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_requested.v1';
  readonly meetingId: string;
  readonly invitationId: string;
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly expiresAtUtc: string;
}
