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
          // Sin esta linea `reschedule()` movia la reunion solo en memoria: la fila conservaba la hora
          // vieja, el evento salia con la nueva y los invitados quedaban avisados de una hora que la
          // sala no tenia.
          ScheduledForUtc: snapshot.scheduledForUtc,
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
            ActorType: p.actorType,
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
            ActorType: p.actorType,
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

  async listActiveHostedBy(tenantId: string, hostUserId: string): Promise<Meeting[]> {
    const rows = await this.prisma.meeting.findMany({
      where: { TenantId: tenantId, HostUserId: hostUserId, Status: { in: ['Scheduled', 'Live'] } },
      include: { Participants: true },
      orderBy: { CreatedAtUtc: 'asc' },
    });
    return rows.map((row) => toDomainMeeting(row, row.Participants));
  }

  async countActiveHostedBy(tenantId: string, hostUserId: string): Promise<number> {
    return this.prisma.meeting.count({
      where: { TenantId: tenantId, HostUserId: hostUserId, Status: { in: ['Scheduled', 'Live'] } },
    });
  }

  async findByShortCodeAnyTenant(shortCode: string): Promise<Meeting | null> {
    const row = await this.prisma.meeting.findFirst({ where: { ShortCode: shortCode } });
    if (!row) return null;
    const participants = await this.prisma.meetingParticipant.findMany({
      where: { MeetingId: row.Id, TenantId: row.TenantId },
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
        InviteeKind: s.inviteeKind,
        InviteeEmail: s.inviteeEmail,
        InviteeUserId: s.inviteeUserId,
        InviteeName: s.inviteeName,
        InviteeExternalPhone: s.inviteeExternalPhone,
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

  async findInvitationById(tenantId: string, invitationId: string): Promise<MeetingInvitation | null> {
    const row = await this.prisma.meetingInvitation.findFirst({
      where: { Id: invitationId, TenantId: tenantId },
    });
    return row ? toDomainMeetingInvitation(row) : null;
  }

  async listInvitationsByMeeting(tenantId: string, meetingId: string): Promise<MeetingInvitation[]> {
    const rows = await this.prisma.meetingInvitation.findMany({
      where: { MeetingId: meetingId, TenantId: tenantId },
      orderBy: { CreatedAtUtc: 'desc' },
    });
    return rows.map(toDomainMeetingInvitation);
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
        OR: [
          { HostUserId: input.userId },
          { Participants: { some: { UserId: input.userId } } },
          // Invitado (aun no unido): sin esto, un empleado/cliente invitado no veia el meeting en
          // su lista hasta unirse (no era Participant). RevokedAtUtc:null excluye invitaciones anuladas.
          { Invitations: { some: { InviteeUserId: input.userId, RevokedAtUtc: null } } },
        ],
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
        OR: [
          { HostUserId: userId },
          { Participants: { some: { UserId: userId } } },
          { Invitations: { some: { InviteeUserId: userId, RevokedAtUtc: null } } },
        ],
      },
    });
  }

  async listPastForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<MeetingSnapshot[]> {
    const rows = await this.prisma.meeting.findMany({
      where: {
        TenantId: input.tenantId,
        Status: { in: ['Ended', 'Cancelled'] },
        OR: [
          { HostUserId: input.userId },
          { Participants: { some: { UserId: input.userId } } },
          { Invitations: { some: { InviteeUserId: input.userId, RevokedAtUtc: null } } },
        ],
      },
      include: { Participants: true },
      // CreatedAtUtc siempre esta poblado (a diferencia de EndedAtUtc, que
      // un meeting Cancelled sin haber arrancado nunca tiene) — mismo
      // criterio de orden seguro que el resto del repo.
      orderBy: { CreatedAtUtc: 'desc' },
      take: input.take,
      skip: input.skip,
    });
    return rows.map((row) => toDomainMeeting(row, row.Participants).toSnapshot());
  }

  async countPastForUser(tenantId: string, userId: string): Promise<number> {
    return this.prisma.meeting.count({
      where: {
        TenantId: tenantId,
        Status: { in: ['Ended', 'Cancelled'] },
        OR: [
          { HostUserId: userId },
          { Participants: { some: { UserId: userId } } },
          { Invitations: { some: { InviteeUserId: userId, RevokedAtUtc: null } } },
        ],
      },
    });
  }

  async getStatsForUser(input: {
    tenantId: string;
    userId: string;
    nowUtc: Date;
    dayStartUtc: Date;
    dayEndUtc: Date;
    weekEndUtc: Date;
  }): Promise<{ today: number; thisWeek: number; liveNow: number; transcriptsAvailable: number }> {
    // Membresía: host, participante, o invitado no revocado — igual que los listados.
    const membership = {
      OR: [
        { HostUserId: input.userId },
        { Participants: { some: { UserId: input.userId } } },
        { Invitations: { some: { InviteeUserId: input.userId, RevokedAtUtc: null } } },
      ],
    };
    const [liveNow, scheduledToday, scheduledThisWeek, transcriptsAvailable] = await Promise.all([
      this.prisma.meeting.count({ where: { TenantId: input.tenantId, Status: 'Live', ...membership } }),
      this.prisma.meeting.count({
        where: {
          TenantId: input.tenantId,
          Status: 'Scheduled',
          ScheduledForUtc: { gte: input.dayStartUtc, lt: input.dayEndUtc },
          ...membership,
        },
      }),
      this.prisma.meeting.count({
        where: {
          TenantId: input.tenantId,
          Status: 'Scheduled',
          ScheduledForUtc: { gte: input.nowUtc, lt: input.weekEndUtc },
          ...membership,
        },
      }),
      this.prisma.meeting.count({
        where: {
          TenantId: input.tenantId,
          Status: 'Ended',
          TranscriptFileId: { not: null },
          ...membership,
        },
      }),
    ]);
    // Un meeting en vivo cuenta como "hoy" y "esta semana" (está pasando ahora).
    return {
      today: liveNow + scheduledToday,
      thisWeek: liveNow + scheduledThisWeek,
      liveNow,
      transcriptsAvailable,
    };
  }
}
