import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../shared/result.js';
import type { MeetingRole, ParticipantStatus } from './meeting-enums.js';
import type { ConnectionQuality, MediaStatus } from '../calls/media-status.js';

export interface MeetingParticipantSnapshot {
  readonly id: string;
  readonly meetingId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly displayName: string;
  readonly role: MeetingRole;
  readonly status: ParticipantStatus;
  readonly joinOrder: number;
  readonly requestedAtUtc: Date;
  readonly admittedAtUtc: Date | null;
  readonly joinedAtUtc: Date | null;
  readonly leftAtUtc: Date | null;
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
  readonly handRaised: boolean;
  readonly connectionQuality: ConnectionQuality;
}

export class MeetingParticipant {
  private constructor(private state: MeetingParticipantSnapshot) {}

  static rehydrate(snapshot: MeetingParticipantSnapshot): MeetingParticipant {
    return new MeetingParticipant(snapshot);
  }

  static create(input: {
    meetingId: string;
    tenantId: string;
    userId: string;
    displayName: string;
    role: MeetingRole;
    status: ParticipantStatus;
    joinOrder: number;
    audioDefault: boolean;
    videoDefault: boolean;
    now: Date;
  }): MeetingParticipant {
    return new MeetingParticipant({
      id: randomUUID(),
      meetingId: input.meetingId,
      tenantId: input.tenantId,
      userId: input.userId,
      displayName: input.displayName.trim().slice(0, 120),
      role: input.role,
      status: input.status,
      joinOrder: input.joinOrder,
      requestedAtUtc: input.now,
      admittedAtUtc: input.status === 'Joined' ? input.now : null,
      joinedAtUtc: input.status === 'Joined' ? input.now : null,
      leftAtUtc: null,
      audioEnabled: input.audioDefault,
      videoEnabled: input.videoDefault,
      screenSharing: false,
      handRaised: false,
      connectionQuality: 'Unknown',
    });
  }

  admit(now: Date): Result<void> {
    if (this.state.status !== 'Waiting') {
      return Result.fail(makeError('Meeting.Participant.NotWaiting', `Cannot admit a ${this.state.status} participant.`));
    }
    this.state = { ...this.state, status: 'Joined', admittedAtUtc: now, joinedAtUtc: now };
    return Result.okVoid();
  }

  markLeft(now: Date): void {
    if (this.state.status === 'Left' || this.state.status === 'Removed') return;
    this.state = { ...this.state, status: 'Left', leftAtUtc: now };
  }

  remove(now: Date): void {
    if (this.state.status === 'Removed') return;
    this.state = { ...this.state, status: 'Removed', leftAtUtc: now };
  }

  applyMediaStatus(status: MediaStatus): void {
    if (this.state.status !== 'Joined') return;
    this.state = {
      ...this.state,
      audioEnabled: status.audioEnabled,
      videoEnabled: status.videoEnabled,
      screenSharing: status.screenSharing,
    };
  }

  forceMuteAudio(): void {
    if (this.state.status !== 'Joined') return;
    this.state = { ...this.state, audioEnabled: false };
  }

  setHandRaised(value: boolean): void {
    if (this.state.status !== 'Joined') return;
    this.state = { ...this.state, handRaised: value };
  }

  promoteToCohost(): Result<void> {
    if (this.state.role === 'Host') {
      return Result.fail(makeError('Meeting.Role.HostCannotBeCohost', 'Host cannot be cohost.'));
    }
    this.state = { ...this.state, role: 'Cohost' };
    return Result.okVoid();
  }

  demoteToAttendee(): Result<void> {
    if (this.state.role === 'Host') {
      return Result.fail(makeError('Meeting.Role.CannotDemoteHost', 'Cannot demote host.'));
    }
    this.state = { ...this.state, role: 'Attendee' };
    return Result.okVoid();
  }

  transferHost(): void {
    this.state = { ...this.state, role: 'Host' };
  }

  demoteHostToCohost(): void {
    this.state = { ...this.state, role: 'Cohost' };
  }

  updateConnectionQuality(quality: ConnectionQuality): void {
    if (this.state.status !== 'Joined') return;
    this.state = { ...this.state, connectionQuality: quality };
  }

  toSnapshot(): MeetingParticipantSnapshot {
    return this.state;
  }

  get userId(): string {
    return this.state.userId;
  }
  get role(): MeetingRole {
    return this.state.role;
  }
  get status(): ParticipantStatus {
    return this.state.status;
  }
  get isJoined(): boolean {
    return this.state.status === 'Joined';
  }
  get joinOrder(): number {
    return this.state.joinOrder;
  }
}
