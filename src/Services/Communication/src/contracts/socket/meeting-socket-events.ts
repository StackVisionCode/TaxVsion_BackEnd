import { z } from 'zod';

export const JoinMeetingPayloadSchema = z.object({
  clientKey: z.string().min(1).max(128),
  meetingId: z.string().uuid(),
  passcode: z.string().max(120).optional(),
  invitationToken: z.string().length(64).optional(),
});
export type JoinMeetingPayload = z.infer<typeof JoinMeetingPayloadSchema>;

export const LeaveMeetingPayloadSchema = z.object({ meetingId: z.string().uuid() });
export type LeaveMeetingPayload = z.infer<typeof LeaveMeetingPayloadSchema>;

export const AdmitPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetUserId: z.string().uuid(),
});
export type AdmitPayload = z.infer<typeof AdmitPayloadSchema>;

export const RemovePayloadSchema = AdmitPayloadSchema;
export type RemovePayload = z.infer<typeof RemovePayloadSchema>;

export const LockPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  locked: z.boolean(),
});
export type LockPayload = z.infer<typeof LockPayloadSchema>;

export const TransferHostPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  newHostUserId: z.string().uuid(),
});
export type TransferHostPayload = z.infer<typeof TransferHostPayloadSchema>;

export const MeetingSignalPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  targetPeerUserId: z.string().uuid(),
  kind: z.enum(['offer', 'answer', 'ice']),
  data: z.record(z.string(), z.unknown()),
});
export type MeetingSignalPayload = z.infer<typeof MeetingSignalPayloadSchema>;

export const MeetingMediaStatusPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  audioEnabled: z.boolean(),
  videoEnabled: z.boolean(),
  screenSharing: z.boolean(),
});
export type MeetingMediaStatusPayload = z.infer<typeof MeetingMediaStatusPayloadSchema>;

export const MeetingRaiseHandPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  raised: z.boolean(),
});
export type MeetingRaiseHandPayload = z.infer<typeof MeetingRaiseHandPayloadSchema>;

export const DominantSpeakerPayloadSchema = z.object({
  meetingId: z.string().uuid(),
  audioLevel: z.number().min(0).max(1),
});
export type DominantSpeakerPayload = z.infer<typeof DominantSpeakerPayloadSchema>;

// ---------- DTOs server -> client ----------

export interface MeetingParticipantDto {
  userId: string;
  displayName: string;
  role: 'Host' | 'Cohost' | 'Attendee';
  status: 'Waiting' | 'Joined' | 'Left' | 'Removed';
  joinOrder: number;
  audioEnabled: boolean;
  videoEnabled: boolean;
  screenSharing: boolean;
  handRaised: boolean;
}

export interface MeetingSnapshotDto {
  meetingId: string;
  status: 'Scheduled' | 'Live' | 'Ended' | 'Cancelled';
  strategy: 'Mesh' | 'Sfu';
  hostUserId: string;
  isLocked: boolean;
  participants: readonly MeetingParticipantDto[];
  yourRole: 'Host' | 'Cohost' | 'Attendee';
  sequence: number;
}

export interface MeetingParticipantChangedDto {
  meetingId: string;
  participant: MeetingParticipantDto;
  sequence: number;
}

export interface MeetingSignalDto {
  meetingId: string;
  fromPeerUserId: string;
  kind: 'offer' | 'answer' | 'ice';
  data: Record<string, unknown>;
}

export interface MeetingStateDto {
  meetingId: string;
  status: 'Scheduled' | 'Live' | 'Ended' | 'Cancelled';
  isLocked: boolean;
  hostUserId: string;
  sequence: number;
}

export interface MeetingDominantSpeakerDto {
  meetingId: string;
  peerUserId: string;
  audioLevel: number;
}

export const MeetingSocketEvents = {
  // c -> s
  Join: 'meeting.join',
  Leave: 'meeting.leave',
  Admit: 'meeting.host.admit',
  Remove: 'meeting.host.remove',
  Lock: 'meeting.host.lock',
  MuteAll: 'meeting.host.mute_all',
  TransferHost: 'meeting.host.transfer',
  Signal: 'meeting.signal',
  MediaStatus: 'meeting.media_status',
  RaiseHand: 'meeting.raise_hand',
  DominantSpeaker: 'meeting.dominant_speaker',
  // s -> c
  Snapshot: 'meeting.snapshot',
  ParticipantChanged: 'meeting.participant.changed',
  StateChanged: 'meeting.state.changed',
  SignalFrom: 'meeting.signal.from',
  DominantSpeakerChanged: 'meeting.dominant_speaker.changed',
  MutedByHost: 'meeting.you.muted',
} as const;
