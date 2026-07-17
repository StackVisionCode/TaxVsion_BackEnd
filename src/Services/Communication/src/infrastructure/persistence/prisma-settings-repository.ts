import type { PrismaClient } from '@prisma/client';
import {
  TenantCommunicationSettings,
  type TenantCommunicationSettingsSnapshot,
} from '../../domain/settings/tenant-communication-settings.js';
import type { TenantCommunicationLimitsSnapshot } from '../../domain/settings/tenant-communication-limits.js';
import type { LimitsRepository, SettingsRepository } from '../../application/ports/settings-repository.js';
import { RecordingConsentPolicy, isRecordingConsentPolicy } from '../../domain/recording/recording-consent.js';

export class PrismaSettingsRepository implements SettingsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findByTenantId(tenantId: string): Promise<TenantCommunicationSettings | null> {
    const row = await this.prisma.tenantCommunicationSettings.findUnique({
      where: { TenantId: tenantId },
    });
    return row ? TenantCommunicationSettings.rehydrate(this.toSnapshot(row)) : null;
  }

  async save(settings: TenantCommunicationSettings): Promise<void> {
    const s = settings.toSnapshot();
    await this.prisma.tenantCommunicationSettings.upsert({
      where: { TenantId: s.tenantId },
      create: {
        TenantId: s.tenantId,
        ChatEnabled: s.chatEnabled,
        CallsEnabled: s.callsEnabled,
        VideoCallsEnabled: s.videoCallsEnabled,
        MeetingsEnabled: s.meetingsEnabled,
        SupportEnabled: s.supportEnabled,
        ScreenshotsEnabled: s.screenshotsEnabled,
        InternalGroupsEnabled: s.internalGroupsEnabled,
        EmployeeToEmployeeChatEnabled: s.employeeToEmployeeChatEnabled,
        DefaultCameraOff: s.defaultCameraOff,
        DefaultMicrophoneOff: s.defaultMicrophoneOff,
        PersistChatOnEnd: s.persistChatOnEnd,
        MessageRetentionDays: s.messageRetentionDays,
        RecordingRetentionDays: s.recordingRetentionDays,
        PurgeEnabled: s.purgeEnabled,
        RecordingConsentPolicy: s.recordingConsentPolicy,
      },
      update: {
        ChatEnabled: s.chatEnabled,
        CallsEnabled: s.callsEnabled,
        VideoCallsEnabled: s.videoCallsEnabled,
        MeetingsEnabled: s.meetingsEnabled,
        SupportEnabled: s.supportEnabled,
        ScreenshotsEnabled: s.screenshotsEnabled,
        InternalGroupsEnabled: s.internalGroupsEnabled,
        EmployeeToEmployeeChatEnabled: s.employeeToEmployeeChatEnabled,
        DefaultCameraOff: s.defaultCameraOff,
        DefaultMicrophoneOff: s.defaultMicrophoneOff,
        PersistChatOnEnd: s.persistChatOnEnd,
        MessageRetentionDays: s.messageRetentionDays,
        RecordingRetentionDays: s.recordingRetentionDays,
        PurgeEnabled: s.purgeEnabled,
        RecordingConsentPolicy: s.recordingConsentPolicy,
      },
    });
  }

  async listPurgeEnabled(): Promise<Array<{ tenantId: string; messageRetentionDays: number }>> {
    const rows = await this.prisma.tenantCommunicationSettings.findMany({
      where: { PurgeEnabled: true },
      select: { TenantId: true, MessageRetentionDays: true },
    });
    return rows.map((r) => ({ tenantId: r.TenantId, messageRetentionDays: r.MessageRetentionDays }));
  }

  private toSnapshot(row: {
    TenantId: string;
    ChatEnabled: boolean;
    CallsEnabled: boolean;
    VideoCallsEnabled: boolean;
    MeetingsEnabled: boolean;
    SupportEnabled: boolean;
    ScreenshotsEnabled: boolean;
    InternalGroupsEnabled: boolean;
    EmployeeToEmployeeChatEnabled: boolean;
    DefaultCameraOff: boolean;
    DefaultMicrophoneOff: boolean;
    PersistChatOnEnd: boolean;
    MessageRetentionDays: number;
    RecordingRetentionDays: number;
    PurgeEnabled: boolean;
    RecordingConsentPolicy: string;
    CreatedAtUtc: Date;
    UpdatedAtUtc: Date;
  }): TenantCommunicationSettingsSnapshot {
    return {
      tenantId: row.TenantId,
      chatEnabled: row.ChatEnabled,
      callsEnabled: row.CallsEnabled,
      videoCallsEnabled: row.VideoCallsEnabled,
      meetingsEnabled: row.MeetingsEnabled,
      supportEnabled: row.SupportEnabled,
      screenshotsEnabled: row.ScreenshotsEnabled,
      internalGroupsEnabled: row.InternalGroupsEnabled,
      employeeToEmployeeChatEnabled: row.EmployeeToEmployeeChatEnabled,
      defaultCameraOff: row.DefaultCameraOff,
      defaultMicrophoneOff: row.DefaultMicrophoneOff,
      persistChatOnEnd: row.PersistChatOnEnd,
      messageRetentionDays: row.MessageRetentionDays,
      recordingRetentionDays: row.RecordingRetentionDays,
      purgeEnabled: row.PurgeEnabled,
      recordingConsentPolicy: isRecordingConsentPolicy(row.RecordingConsentPolicy)
        ? row.RecordingConsentPolicy
        : RecordingConsentPolicy.NoRejections,
      createdAtUtc: row.CreatedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }
}

export class PrismaLimitsRepository implements LimitsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findByTenantId(tenantId: string): Promise<TenantCommunicationLimitsSnapshot | null> {
    const row = await this.prisma.tenantCommunicationLimits.findUnique({ where: { TenantId: tenantId } });
    if (!row) return null;
    return {
      tenantId: row.TenantId,
      planCode: row.PlanCode,
      maxMeetingParticipants: row.MaxMeetingParticipants,
      maxMeetingMinutes: row.MaxMeetingMinutes,
      maxConcurrentCalls: row.MaxConcurrentCalls,
      maxMonthlyMinutes: row.MaxMonthlyMinutes,
      recordingEnabled: row.RecordingEnabled,
      supportEnabled: row.SupportEnabled,
      isSuspended: row.IsSuspended,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }

  async upsert(s: TenantCommunicationLimitsSnapshot): Promise<void> {
    await this.prisma.tenantCommunicationLimits.upsert({
      where: { TenantId: s.tenantId },
      create: {
        TenantId: s.tenantId,
        PlanCode: s.planCode,
        MaxMeetingParticipants: s.maxMeetingParticipants,
        MaxMeetingMinutes: s.maxMeetingMinutes,
        MaxConcurrentCalls: s.maxConcurrentCalls,
        MaxMonthlyMinutes: s.maxMonthlyMinutes,
        RecordingEnabled: s.recordingEnabled,
        SupportEnabled: s.supportEnabled,
        IsSuspended: s.isSuspended,
      },
      update: {
        PlanCode: s.planCode,
        MaxMeetingParticipants: s.maxMeetingParticipants,
        MaxMeetingMinutes: s.maxMeetingMinutes,
        MaxConcurrentCalls: s.maxConcurrentCalls,
        MaxMonthlyMinutes: s.maxMonthlyMinutes,
        RecordingEnabled: s.recordingEnabled,
        SupportEnabled: s.supportEnabled,
        IsSuspended: s.isSuspended,
      },
    });
  }

  async markSuspended(tenantId: string, suspended: boolean, _now: Date): Promise<void> {
    await this.prisma.tenantCommunicationLimits.update({
      where: { TenantId: tenantId },
      data: { IsSuspended: suspended },
    }).catch(() => undefined);
  }
}
