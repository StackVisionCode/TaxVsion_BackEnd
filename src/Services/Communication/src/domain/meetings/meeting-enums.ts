export const MeetingStatus = {
  Scheduled: 'Scheduled',
  Live: 'Live',
  Ended: 'Ended',
  Cancelled: 'Cancelled',
  // Agendada que nunca se inició y venció (D-35); terminal como Ended/Cancelled.
  Expired: 'Expired',
} as const;
export type MeetingStatus = (typeof MeetingStatus)[keyof typeof MeetingStatus];

export function isMeetingStatus(value: string): value is MeetingStatus {
  return (
    value === 'Scheduled' ||
    value === 'Live' ||
    value === 'Ended' ||
    value === 'Cancelled' ||
    value === 'Expired'
  );
}

/** Estados en los que la reunión ya no admite a nadie. */
export function isTerminalMeetingStatus(value: MeetingStatus): boolean {
  return value === 'Ended' || value === 'Cancelled' || value === 'Expired';
}

/**
 * Por qué terminó una reunión Live (INV-11 §13.1). `Host` = End explícito de host/cohost; el resto
 * lo decide el sistema: tope de duración (D-34/D-35), sala vacía, retiro del host (offboarding) o el
 * job de limpieza de reuniones eternas (D-60).
 */
export const MeetingEndReason = {
  Host: 'Host',
  MaxDurationReached: 'MaxDurationReached',
  NoParticipants: 'NoParticipants',
  Offboarding: 'Offboarding',
  Backfill: 'Backfill',
} as const;
export type MeetingEndReason = (typeof MeetingEndReason)[keyof typeof MeetingEndReason];

export function isMeetingEndReason(value: string): value is MeetingEndReason {
  return (
    value === 'Host' ||
    value === 'MaxDurationReached' ||
    value === 'NoParticipants' ||
    value === 'Offboarding' ||
    value === 'Backfill'
  );
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
