import { parseReactionCatalogColumn } from './reaction-catalog-column.js';
import type { PrismaClient } from '@prisma/client';
import {
  TenantCommunicationSettings,
  type TenantCommunicationSettingsSnapshot,
} from '../../domain/settings/tenant-communication-settings.js';
import type { TenantCommunicationLimitsSnapshot } from '../../domain/settings/tenant-communication-limits.js';
import type { LimitsRepository, SettingsRepository } from '../../application/ports/settings-repository.js';
import { RecordingConsentPolicy, isRecordingConsentPolicy } from '../../domain/recording/recording-consent.js';
import { isMeetingSoundsPolicy } from '../../domain/settings/technical-limits.js';

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
    const data = {
      ChatEnabled: s.chatEnabled,
      CallsEnabled: s.callsEnabled,
      VideoCallsEnabled: s.videoCallsEnabled,
      MeetingsEnabled: s.meetingsEnabled,
      SupportEnabled: s.supportEnabled,
      ScreenshotsEnabled: s.screenshotsEnabled,
      EmployeeToEmployeeChatEnabled: s.employeeToEmployeeChatEnabled,
      RestrictCustomerChatToAssignedPreparer: s.restrictCustomerChatToAssignedPreparer,
      GroupsAllowMultipleClients: s.groupsAllowMultipleClients,
      DefaultCameraOff: s.defaultCameraOff,
      DefaultMicrophoneOff: s.defaultMicrophoneOff,
      PersistChatOnEnd: s.persistChatOnEnd,
      MessageRetentionDays: s.messageRetentionDays,
      RecordingRetentionDays: s.recordingRetentionDays,
      PurgeEnabled: s.purgeEnabled,
      RecordingConsentPolicy: s.recordingConsentPolicy,
      DefaultMeetingDurationMinutes: s.defaultMeetingDurationMinutes,
      MaxMeetingDurationMinutes: s.maxMeetingDurationMinutes,
      MaxMeetingParticipants: s.maxMeetingParticipants,
      MaxRecordingMinutesMeeting: s.maxRecordingMinutesMeeting,
      MaxRecordingMinutesAudioCall: s.maxRecordingMinutesAudioCall,
      MaxRecordingMinutesVideoCall: s.maxRecordingMinutesVideoCall,
      RingingTimeoutSeconds: s.ringingTimeoutSeconds,
      MeetingChatAttachmentsPerMinute: s.meetingChatAttachmentsPerMinute,
      MeetingChatAttachmentsPerMeeting: s.meetingChatAttachmentsPerMeeting,
      MeetingSoundsPolicy: s.meetingSoundsPolicy,
      TranscriptLanguage: s.transcriptLanguage,
      RecordingVisibleInPortal: s.recordingVisibleInPortal,
      GuestsAllowed: s.guestsAllowed,
      ClientsCanShareScreen: s.clientsCanShareScreen,
      ScreenAudioAllowed: s.screenAudioAllowed,
      AudioEchoCancellationDefault: s.audioEchoCancellationDefault,
      AudioNoiseSuppressionDefault: s.audioNoiseSuppressionDefault,
      AudioAutoGainControlDefault: s.audioAutoGainControlDefault,
      EchoMuteOnDetect: s.echoMuteOnDetect,
      ReactionsEnabled: s.reactionsEnabled,
      MeetingReactionCatalog: s.meetingReactionCatalog === null ? null : JSON.stringify(s.meetingReactionCatalog),
      MeetingReminderMinutesBefore: s.meetingReminderMinutesBefore,
      GuestsWaitingRoom: s.guestsWaitingRoom,
      TranscriptPrompt: s.transcriptPrompt,
      VoiceNoteTranscriptEnabled: s.voiceNoteTranscriptEnabled,
      ChatEditWindowMinutesDirect: s.chatEditWindowMinutesDirect,
      ChatEditWindowMinutesGroup: s.chatEditWindowMinutesGroup,
      ChatEditWindowMinutesSupport: s.chatEditWindowMinutesSupport,
      ChatEditWindowMinutesMeeting: s.chatEditWindowMinutesMeeting,
      ChatMaxEditsPerMessage: s.chatMaxEditsPerMessage,
    };
    await this.prisma.tenantCommunicationSettings.upsert({
      where: { TenantId: s.tenantId },
      create: { TenantId: s.tenantId, ...data },
      update: data,
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
    EmployeeToEmployeeChatEnabled: boolean;
    RestrictCustomerChatToAssignedPreparer: boolean;
    GroupsAllowMultipleClients: boolean;
    DefaultCameraOff: boolean;
    DefaultMicrophoneOff: boolean;
    PersistChatOnEnd: boolean;
    MessageRetentionDays: number;
    RecordingRetentionDays: number;
    PurgeEnabled: boolean;
    RecordingConsentPolicy: string;
    DefaultMeetingDurationMinutes: number | null;
    MaxMeetingDurationMinutes: number | null;
    MaxMeetingParticipants: number | null;
    MaxRecordingMinutesMeeting: number | null;
    MaxRecordingMinutesAudioCall: number | null;
    MaxRecordingMinutesVideoCall: number | null;
    RingingTimeoutSeconds: number | null;
    MeetingChatAttachmentsPerMinute: number | null;
    MeetingChatAttachmentsPerMeeting: number | null;
    MeetingSoundsPolicy: string | null;
    TranscriptLanguage: string | null;
    RecordingVisibleInPortal: boolean;
    GuestsAllowed: boolean;
    ClientsCanShareScreen: boolean | null;
    ScreenAudioAllowed: boolean | null;
    AudioEchoCancellationDefault: boolean | null;
    AudioNoiseSuppressionDefault: boolean | null;
    AudioAutoGainControlDefault: boolean | null;
    EchoMuteOnDetect: boolean | null;
    ReactionsEnabled: boolean | null;
    MeetingReactionCatalog: string | null;
    MeetingReminderMinutesBefore: number | null;
    GuestsWaitingRoom: boolean | null;
    TranscriptPrompt: string | null;
    VoiceNoteTranscriptEnabled: boolean | null;
    ChatEditWindowMinutesDirect: number | null;
    ChatEditWindowMinutesGroup: number | null;
    ChatEditWindowMinutesSupport: number | null;
    ChatEditWindowMinutesMeeting: number | null;
    ChatMaxEditsPerMessage: number | null;
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
      employeeToEmployeeChatEnabled: row.EmployeeToEmployeeChatEnabled,
      restrictCustomerChatToAssignedPreparer: row.RestrictCustomerChatToAssignedPreparer,
      groupsAllowMultipleClients: row.GroupsAllowMultipleClients,
      defaultCameraOff: row.DefaultCameraOff,
      defaultMicrophoneOff: row.DefaultMicrophoneOff,
      persistChatOnEnd: row.PersistChatOnEnd,
      messageRetentionDays: row.MessageRetentionDays,
      recordingRetentionDays: row.RecordingRetentionDays,
      purgeEnabled: row.PurgeEnabled,
      recordingConsentPolicy: isRecordingConsentPolicy(row.RecordingConsentPolicy)
        ? row.RecordingConsentPolicy
        : RecordingConsentPolicy.NoRejections,
      defaultMeetingDurationMinutes: row.DefaultMeetingDurationMinutes,
      maxMeetingDurationMinutes: row.MaxMeetingDurationMinutes,
      maxMeetingParticipants: row.MaxMeetingParticipants,
      maxRecordingMinutesMeeting: row.MaxRecordingMinutesMeeting,
      maxRecordingMinutesAudioCall: row.MaxRecordingMinutesAudioCall,
      maxRecordingMinutesVideoCall: row.MaxRecordingMinutesVideoCall,
      ringingTimeoutSeconds: row.RingingTimeoutSeconds,
      meetingChatAttachmentsPerMinute: row.MeetingChatAttachmentsPerMinute,
      meetingChatAttachmentsPerMeeting: row.MeetingChatAttachmentsPerMeeting,
      meetingSoundsPolicy: isMeetingSoundsPolicy(row.MeetingSoundsPolicy) ? row.MeetingSoundsPolicy : null,
      transcriptLanguage: row.TranscriptLanguage,
      recordingVisibleInPortal: row.RecordingVisibleInPortal,
      guestsAllowed: row.GuestsAllowed,
      clientsCanShareScreen: row.ClientsCanShareScreen,
      screenAudioAllowed: row.ScreenAudioAllowed,
      audioEchoCancellationDefault: row.AudioEchoCancellationDefault,
      audioNoiseSuppressionDefault: row.AudioNoiseSuppressionDefault,
      audioAutoGainControlDefault: row.AudioAutoGainControlDefault,
      echoMuteOnDetect: row.EchoMuteOnDetect,
      reactionsEnabled: row.ReactionsEnabled,
      meetingReactionCatalog: parseReactionCatalogColumn(row.MeetingReactionCatalog),
      meetingReminderMinutesBefore: row.MeetingReminderMinutesBefore,
      guestsWaitingRoom: row.GuestsWaitingRoom,
      transcriptPrompt: row.TranscriptPrompt,
      voiceNoteTranscriptEnabled: row.VoiceNoteTranscriptEnabled,
      chatEditWindowMinutesDirect: row.ChatEditWindowMinutesDirect,
      chatEditWindowMinutesGroup: row.ChatEditWindowMinutesGroup,
      chatEditWindowMinutesSupport: row.ChatEditWindowMinutesSupport,
      chatEditWindowMinutesMeeting: row.ChatEditWindowMinutesMeeting,
      chatMaxEditsPerMessage: row.ChatMaxEditsPerMessage,
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
      enabledModules: parseEnabledModules(row.EnabledModulesJson),
      maxRecordingMinutesMeeting: row.MaxRecordingMinutesMeeting,
      maxRecordingMinutesAudioCall: row.MaxRecordingMinutesAudioCall,
      maxRecordingMinutesVideoCall: row.MaxRecordingMinutesVideoCall,
      recordingVideoEnabled: row.RecordingVideoEnabled,
      transcriptEnabled: row.TranscriptEnabled,
      callsEnabled: row.CallsEnabled,
      maxCallMinutes: row.MaxCallMinutes,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }

  async upsert(s: TenantCommunicationLimitsSnapshot): Promise<void> {
    const data = {
      PlanCode: s.planCode,
      MaxMeetingParticipants: s.maxMeetingParticipants,
      MaxMeetingMinutes: s.maxMeetingMinutes,
      MaxConcurrentCalls: s.maxConcurrentCalls,
      MaxMonthlyMinutes: s.maxMonthlyMinutes,
      RecordingEnabled: s.recordingEnabled,
      SupportEnabled: s.supportEnabled,
      IsSuspended: s.isSuspended,
      EnabledModulesJson: JSON.stringify(s.enabledModules),
      MaxRecordingMinutesMeeting: s.maxRecordingMinutesMeeting,
      MaxRecordingMinutesAudioCall: s.maxRecordingMinutesAudioCall,
      MaxRecordingMinutesVideoCall: s.maxRecordingMinutesVideoCall,
      RecordingVideoEnabled: s.recordingVideoEnabled,
      TranscriptEnabled: s.transcriptEnabled,
      CallsEnabled: s.callsEnabled,
      MaxCallMinutes: s.maxCallMinutes,
    };
    await this.prisma.tenantCommunicationLimits.upsert({
      where: { TenantId: s.tenantId },
      create: { TenantId: s.tenantId, ...data },
      update: data,
    });
  }

  async markSuspended(tenantId: string, suspended: boolean, _now: Date): Promise<void> {
    await this.prisma.tenantCommunicationLimits.update({
      where: { TenantId: tenantId },
      data: { IsSuspended: suspended },
    }).catch(() => undefined);
  }
}

/**
 * Deserializa `EnabledModulesJson`. Tolera "" (default de columna en filas preexistentes a la
 * migracion), null y JSON invalido -> [], y descarta entradas no-string.
 */
function parseEnabledModules(json: string | null | undefined): string[] {
  if (!json) return [];
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : [];
  } catch {
    return [];
  }
}
