import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events del ciclo de vida de un meeting. NUNCA llevan SDP/ICE/media.
 * Consumidores: Notification (invitaciones, cancelaciones), Planner (sync de slots),
 * Analytics (participant-minutes para facturación), Compliance (audit de grabación).
 */
export const MeetingEventTypes = {
  Scheduled:               'communication.meeting.scheduled.v1',
  Cancelled:               'communication.meeting.cancelled.v1',
  Rescheduled:             'communication.meeting.rescheduled.v1',
  Started:                 'communication.meeting.started.v1',
  Ended:                   'communication.meeting.ended.v1',
  Locked:                  'communication.meeting.locked.v1',
  Unlocked:                'communication.meeting.unlocked.v1',
  HostTransferred:         'communication.meeting.host_transferred.v1',
  ParticipantJoined:       'communication.meeting.participant_joined.v1',
  ParticipantLeft:         'communication.meeting.participant_left.v1',
  ParticipantAdmitted:     'communication.meeting.participant_admitted.v1',
  ParticipantDenied:       'communication.meeting.participant_denied.v1',
  ParticipantRemovedByHost: 'communication.meeting.participant_removed_by_host.v1',
  RecordingStarted:        'communication.meeting.recording_started.v1',
  RecordingStopped:        'communication.meeting.recording_stopped.v1',
  RecordingReady:          'communication.meeting.recording_ready.v1',
  TranscriptReady:         'communication.meeting.transcript_ready.v1',
  InvitationRequested:     'communication.meeting.invitation_requested.v1',
} as const;

export interface MeetingScheduledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.scheduled.v1';
  readonly meetingId: string;
  readonly title: string;
  readonly hostUserId: string;
  readonly scheduledForUtc: string | null;
  readonly shortCode: string;
}

export interface MeetingCancelledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.cancelled.v1';
  readonly meetingId: string;
  readonly cancelledByUserId: string;
  readonly cancelledAtUtc: string;
  /** IDs de participantes para que Notification envíe el email de cancelación. */
  readonly participantUserIds: readonly string[];
}

export interface MeetingRescheduledEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.rescheduled.v1';
  readonly meetingId: string;
  readonly rescheduledByUserId: string;
  readonly previousScheduledForUtc: string | null;
  readonly newScheduledForUtc: string | null;
  readonly rescheduledAtUtc: string;
  readonly participantUserIds: readonly string[];
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
  readonly participantCount: number;
  readonly recordingFileId: string | null;
}

export interface MeetingLockedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.locked.v1';
  readonly meetingId: string;
  readonly lockedByUserId: string;
  readonly lockedAtUtc: string;
}

export interface MeetingUnlockedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.unlocked.v1';
  readonly meetingId: string;
  readonly unlockedByUserId: string;
  readonly unlockedAtUtc: string;
}

export interface MeetingHostTransferredEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.host_transferred.v1';
  readonly meetingId: string;
  readonly previousHostUserId: string;
  readonly newHostUserId: string;
  readonly transferredAtUtc: string;
}

export interface MeetingParticipantJoinedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_joined.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly joinedAtUtc: string;
}

export interface MeetingParticipantLeftEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_left.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly joinedAtUtc: string;
  readonly leftAtUtc: string;
  readonly durationSeconds: number;
}

export interface MeetingParticipantAdmittedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_admitted.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly admittedByUserId: string;
  readonly admittedAtUtc: string;
}

export interface MeetingParticipantDeniedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_denied.v1';
  readonly meetingId: string;
  readonly participantUserId: string;
  readonly deniedByUserId: string;
  readonly deniedAtUtc: string;
}

export interface MeetingParticipantRemovedByHostEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.participant_removed_by_host.v1';
  readonly meetingId: string;
  readonly removedParticipantUserId: string;
  readonly removedByUserId: string;
  readonly removedAtUtc: string;
}

export interface MeetingRecordingStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_started.v1';
  readonly meetingId: string;
  readonly startedByUserId: string;
  readonly recordingStartedAtUtc: string;
}

export interface MeetingRecordingStoppedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_stopped.v1';
  readonly meetingId: string;
  readonly stoppedByUserId: string;
  readonly recordingStartedAtUtc: string;
  readonly recordingStoppedAtUtc: string;
  readonly durationSeconds: number;
}

export interface MeetingRecordingReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.recording_ready.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly durationSeconds: number;
  readonly participantCount: number;
  readonly readyAtUtc: string;
}

/** Ver docblock de CallTranscriptReadyEvent — mismo flujo, para meetings. */
export interface MeetingTranscriptReadyEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.transcript_ready.v1';
  readonly meetingId: string;
  readonly recordingFileId: string;
  readonly transcriptFileId: string;
  readonly language: string | null;
  readonly readyAtUtc: string;
}

export interface MeetingInvitationRequestedEvent extends IntegrationEvent {
  readonly eventType: 'communication.meeting.invitation_requested.v1';
  readonly meetingId: string;
  readonly invitationId: string;
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly expiresAtUtc: string;
}
