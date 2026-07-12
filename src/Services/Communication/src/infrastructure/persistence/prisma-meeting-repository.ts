import type { PrismaClient } from '@prisma/client';
import type { Meeting, MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../../application/ports/meeting-repository.js';
import { toDomainMeeting, toDomainMeetingInvitation } from './meeting-mapper.js';

export class PrismaMeetingRepository implements MeetingRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(meeting: Meeting): Promise<void> {
    const snapshot = meeting.toSnapshot();
    await this.prisma.$transaction(async (tx) => {
      await tx.meeting.upsert({
        where: { Id: snapshot.id },
        create: {
          Id: snapshot.id,
          TenantId: snapshot.tenantId,
          Title: snapshot.title,
          Description: snapshot.description,
          Status: snapshot.status,
          ShortCode: snapshot.shortCode,
          PasscodeHash: snapshot.passcodeHash,
          RequireWaitingRoom: snapshot.requireWaitingRoom,
          IsLocked: snapshot.isLocked,
          MaxParticipants: snapshot.maxParticipants,
          Strategy: snapshot.strategy,
          RecordingRequested: snapshot.recordingRequested,
          RecordingFileId: snapshot.recordingFileId,
          TranscriptFileId: snapshot.transcriptFileId,
          HostUserId: snapshot.hostUserId,
          ScheduledForUtc: snapshot.scheduledForUtc,
          StartedAtUtc: snapshot.startedAtUtc,
          EndedAtUtc: snapshot.endedAtUtc,
          DurationSeconds: snapshot.durationSeconds,
          CreatedByUserId: snapshot.createdByUserId,
          CreatedAtUtc: snapshot.createdAtUtc,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
        update: {
          Title: snapshot.title,
          Description: snapshot.description,
          Status: snapshot.status,
          PasscodeHash: snapshot.passcodeHash,
          RequireWaitingRoom: snapshot.requireWaitingRoom,
          IsLocked: snapshot.isLocked,
          Strategy: snapshot.strategy,
          RecordingFileId: snapshot.recordingFileId,
          TranscriptFileId: snapshot.transcriptFileId,
          HostUserId: snapshot.hostUserId,
          StartedAtUtc: snapshot.startedAtUtc,
          EndedAtUtc: snapshot.endedAtUtc,
          DurationSeconds: snapshot.durationSeconds,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
      });

      for (const p of snapshot.participants) {
        await tx.meetingParticipant.upsert({
          where: { MeetingId_UserId: { MeetingId: snapshot.id, UserId: p.userId } },
          create: {
            Id: p.id,
            MeetingId: snapshot.id,
            TenantId: p.tenantId,
            UserId: p.userId,
            DisplayName: p.displayName,
            Role: p.role,
            Status: p.status,
            JoinOrder: p.joinOrder,
            RequestedAtUtc: p.requestedAtUtc,
            AdmittedAtUtc: p.admittedAtUtc,
            JoinedAtUtc: p.joinedAtUtc,
            LeftAtUtc: p.leftAtUtc,
            AudioEnabled: p.audioEnabled,
            VideoEnabled: p.videoEnabled,
            ScreenSharing: p.screenSharing,
            HandRaised: p.handRaised,
            ConnectionQuality: p.connectionQuality,
          },
          update: {
            DisplayName: p.displayName,
            Role: p.role,
            Status: p.status,
            AdmittedAtUtc: p.admittedAtUtc,
            JoinedAtUtc: p.joinedAtUtc,
            LeftAtUtc: p.leftAtUtc,
            AudioEnabled: p.audioEnabled,
            VideoEnabled: p.videoEnabled,
            ScreenSharing: p.screenSharing,
            HandRaised: p.handRaised,
            ConnectionQuality: p.connectionQuality,
          },
        });
      }
    });
  }

  async findById(tenantId: string, meetingId: string): Promise<Meeting | null> {
    const row = await this.prisma.meeting.findFirst({ where: { Id: meetingId, TenantId: tenantId } });
    if (!row) return null;
    const participants = await this.prisma.meetingParticipant.findMany({
      where: { MeetingId: meetingId, TenantId: tenantId },
    });
    return toDomainMeeting(row, participants);
  }

  async findByShortCode(tenantId: string, shortCode: string): Promise<Meeting | null> {
    const row = await this.prisma.meeting.findFirst({ where: { TenantId: tenantId, ShortCode: shortCode } });
    if (!row) return null;
    const participants = await this.prisma.meetingParticipant.findMany({
      where: { MeetingId: row.Id, TenantId: tenantId },
    });
    return toDomainMeeting(row, participants);
  }

  async saveInvitation(invitation: MeetingInvitation): Promise<void> {
    const s = invitation.toSnapshot();
    await this.prisma.meetingInvitation.upsert({
      where: { Id: s.id },
      create: {
        Id: s.id,
        MeetingId: s.meetingId,
        TenantId: s.tenantId,
        InviteeEmail: s.inviteeEmail,
        InviteeUserId: s.inviteeUserId,
        TokenHash: s.tokenHash,
        ExpiresAtUtc: s.expiresAtUtc,
        UsedAtUtc: s.usedAtUtc,
        RevokedAtUtc: s.revokedAtUtc,
        CreatedAtUtc: s.createdAtUtc,
      },
      update: {
        UsedAtUtc: s.usedAtUtc,
        RevokedAtUtc: s.revokedAtUtc,
      },
    });
  }

  async findInvitationByHash(tokenHash: string): Promise<MeetingInvitation | null> {
    const row = await this.prisma.meetingInvitation.findUnique({ where: { TokenHash: tokenHash } });
    return row ? toDomainMeetingInvitation(row) : null;
  }

  async listUpcomingForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<MeetingSnapshot[]> {
    const rows = await this.prisma.meeting.findMany({
      where: {
        TenantId: input.tenantId,
        Status: { in: ['Scheduled', 'Live'] },
        OR: [{ HostUserId: input.userId }, { Participants: { some: { UserId: input.userId } } }],
      },
      include: { Participants: true },
      orderBy: [{ ScheduledForUtc: 'asc' }, { CreatedAtUtc: 'desc' }],
      take: input.take,
      skip: input.skip,
    });
    return rows.map((row) => toDomainMeeting(row, row.Participants).toSnapshot());
  }

  async countUpcomingForUser(tenantId: string, userId: string): Promise<number> {
    return this.prisma.meeting.count({
      where: {
        TenantId: tenantId,
        Status: { in: ['Scheduled', 'Live'] },
        OR: [{ HostUserId: userId }, { Participants: { some: { UserId: userId } } }],
      },
    });
  }
}
