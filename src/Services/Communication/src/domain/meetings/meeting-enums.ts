export const MeetingStatus = {
  Scheduled: 'Scheduled',
  Live: 'Live',
  Ended: 'Ended',
  Cancelled: 'Cancelled',
} as const;
export type MeetingStatus = (typeof MeetingStatus)[keyof typeof MeetingStatus];

export function isMeetingStatus(value: string): value is MeetingStatus {
  return value === 'Scheduled' || value === 'Live' || value === 'Ended' || value === 'Cancelled';
}

export const MeetingRole = {
  Host: 'Host',
  Cohost: 'Cohost',
  Attendee: 'Attendee',
} as const;
export type MeetingRole = (typeof MeetingRole)[keyof typeof MeetingRole];

export function isMeetingRole(value: string): value is MeetingRole {
  return value === 'Host' || value === 'Cohost' || value === 'Attendee';
}

export const ParticipantStatus = {
  Waiting: 'Waiting',
  Joined: 'Joined',
  Left: 'Left',
  Removed: 'Removed',
} as const;
export type ParticipantStatus = (typeof ParticipantStatus)[keyof typeof ParticipantStatus];

export function isParticipantStatus(value: string): value is ParticipantStatus {
  return value === 'Waiting' || value === 'Joined' || value === 'Left' || value === 'Removed';
}

export const MeetingStrategy = { Mesh: 'Mesh', Sfu: 'Sfu' } as const;
export type MeetingStrategy = (typeof MeetingStrategy)[keyof typeof MeetingStrategy];

export function isMeetingStrategy(value: string): value is MeetingStrategy {
  return value === 'Mesh' || value === 'Sfu';
}
