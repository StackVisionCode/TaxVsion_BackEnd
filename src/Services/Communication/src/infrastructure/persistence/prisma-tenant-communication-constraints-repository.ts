import type { PrismaClient } from '@prisma/client';
import { TenantCommunicationConstraints } from '../../domain/settings/tenant-communication-constraints.js';
import type { TenantCommunicationConstraintsRepository } from '../../application/ports/tenant-communication-constraints-repository.js';

export class PrismaTenantCommunicationConstraintsRepository implements TenantCommunicationConstraintsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findByTenantId(tenantId: string): Promise<TenantCommunicationConstraints | null> {
    const row = await this.prisma.tenantCommunicationConstraints.findUnique({ where: { TenantId: tenantId } });
    if (!row) return null;
    return TenantCommunicationConstraints.rehydrate({
      tenantId: row.TenantId,
      version: row.Version,
      maxMeetingParticipants: row.MaxMeetingParticipants,
      maxMeetingDurationMinutes: row.MaxMeetingDurationMinutes,
      maxRecordingMinutesMeeting: row.MaxRecordingMinutesMeeting,
      maxRecordingMinutesAudioCall: row.MaxRecordingMinutesAudioCall,
      maxRecordingMinutesVideoCall: row.MaxRecordingMinutesVideoCall,
      recordingAllowed: row.RecordingAllowed,
      guestsAllowed: row.GuestsAllowed,
      messageRetentionDays: row.MessageRetentionDays,
      recordingRetentionDays: row.RecordingRetentionDays,
      purgeEnabled: row.PurgeEnabled,
      notes: row.Notes,
      updatedByUserId: row.UpdatedByUserId,
      createdAtUtc: row.CreatedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
    });
  }

  async save(constraints: TenantCommunicationConstraints): Promise<void> {
    const s = constraints.toSnapshot();
    const data = {
      Version: s.version,
      MaxMeetingParticipants: s.maxMeetingParticipants,
      MaxMeetingDurationMinutes: s.maxMeetingDurationMinutes,
      MaxRecordingMinutesMeeting: s.maxRecordingMinutesMeeting,
      MaxRecordingMinutesAudioCall: s.maxRecordingMinutesAudioCall,
      MaxRecordingMinutesVideoCall: s.maxRecordingMinutesVideoCall,
      RecordingAllowed: s.recordingAllowed,
      GuestsAllowed: s.guestsAllowed,
      MessageRetentionDays: s.messageRetentionDays,
      RecordingRetentionDays: s.recordingRetentionDays,
      PurgeEnabled: s.purgeEnabled,
      Notes: s.notes,
      UpdatedByUserId: s.updatedByUserId,
    };
    await this.prisma.tenantCommunicationConstraints.upsert({
      where: { TenantId: s.tenantId },
      create: { TenantId: s.tenantId, ...data },
      update: data,
    });
  }
}
