import { parseReactionCatalogColumn } from './reaction-catalog-column.js';
import type { PrismaClient } from '@prisma/client';
import {
  PlatformCommunicationSettings,
  type PlatformCommunicationSettingsSnapshot,
} from '../../domain/settings/platform-communication-settings.js';
import type { PlatformCommunicationSettingsRepository } from '../../application/ports/platform-communication-settings-repository.js';
import type { MeetingSoundsPolicy } from '../../domain/settings/technical-limits.js';
import { logger } from '../logger/logger.js';

const GLOBAL_ID = 'global';

export class PrismaPlatformCommunicationSettingsRepository implements PlatformCommunicationSettingsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async get(): Promise<PlatformCommunicationSettings> {
    const row = await this.prisma.platformCommunicationSettings.findUnique({ where: { Id: GLOBAL_ID } });
    if (!row) return PlatformCommunicationSettings.defaults();
    const { settings, corrected } = PlatformCommunicationSettings.rehydrate(toSnapshot(row));
    if (corrected.length > 0) {
      // Fail-safe: un valor fuera de rango en BD no se propaga; se lee el fallback y se deja rastro.
      logger.warn({ corrected }, 'PlatformCommunicationSettings: values out of technical range were replaced by fallbacks');
    }
    return settings;
  }

  async save(settings: PlatformCommunicationSettings): Promise<void> {
    const s = settings.toSnapshot();
    const data = {
      Version: s.version,
      MaxMeetingDurationMinutes: s.maxMeetingDurationMinutes,
      DefaultMeetingDurationMinutes: s.defaultMeetingDurationMinutes,
      DefaultMaxParticipants: s.defaultMaxParticipants,
      MeshMaxParticipants: s.meshMaxParticipants,
      DisconnectGraceSeconds: s.disconnectGraceSeconds,
      ScheduledMeetingExpiryHours: s.scheduledMeetingExpiryHours,
      EmptyMeetingEndMinutes: s.emptyMeetingEndMinutes,
      DominantSpeakerMaxPerSecond: s.dominantSpeakerMaxPerSecond,
      GuestsAllowed: s.guestsAllowed,
      RingingTimeoutSeconds: s.ringingTimeoutSeconds,
      RecordingConsentTimeoutMeetingSeconds: s.recordingConsentTimeoutMeetingSeconds,
      RecordingConsentTimeoutCallSeconds: s.recordingConsentTimeoutCallSeconds,
      MaxRecordingMinutesMeeting: s.maxRecordingMinutesMeeting,
      MaxRecordingMinutesAudioCall: s.maxRecordingMinutesAudioCall,
      MaxRecordingMinutesVideoCall: s.maxRecordingMinutesVideoCall,
      InvitationMinTtlHours: s.invitationMinTtlHours,
      InvitationExpiryGraceHours: s.invitationExpiryGraceHours,
      InternalGroupsEnabled: s.internalGroupsEnabled,
      MeetingChatAttachmentsPerMinute: s.meetingChatAttachmentsPerMinute,
      MeetingChatAttachmentsPerMeeting: s.meetingChatAttachmentsPerMeeting,
      MaxReactionsPerMessage: s.maxReactionsPerMessage,
      MeetingSoundsPolicy: s.meetingSoundsPolicy,
      TranscriptDefaultLanguage: s.transcriptDefaultLanguage,
      OutboxRetentionDays: s.outboxRetentionDays,
      ProcessedEventRetentionDays: s.processedEventRetentionDays,
      IdempotencyRetentionDays: s.idempotencyRetentionDays,
      QualitySampleIntervalSeconds: s.qualitySampleIntervalSeconds,
      QualityExcellentRttMs: s.qualityExcellentRttMs,
      QualityGoodRttMs: s.qualityGoodRttMs,
      QualityUnstableRttMs: s.qualityUnstableRttMs,
      QualityExcellentLossPct: s.qualityExcellentLossPct,
      QualityGoodLossPct: s.qualityGoodLossPct,
      QualityUnstableLossPct: s.qualityUnstableLossPct,
      QualityExcellentJitterMs: s.qualityExcellentJitterMs,
      QualityGoodJitterMs: s.qualityGoodJitterMs,
      QualityUnstableJitterMs: s.qualityUnstableJitterMs,
      QualityUpgradeSamples: s.qualityUpgradeSamples,
      QualityPoorSustainedSamples: s.qualityPoorSustainedSamples,
      QualityRecoverySamples: s.qualityRecoverySamples,
      CameraMaxWidth: s.cameraMaxWidth,
      CameraMaxHeight: s.cameraMaxHeight,
      CameraMaxFps: s.cameraMaxFps,
      MeshCameraMaxWidth: s.meshCameraMaxWidth,
      MeshCameraMaxHeight: s.meshCameraMaxHeight,
      MeshCameraMaxFps: s.meshCameraMaxFps,
      ScreenMaxWidth: s.screenMaxWidth,
      ScreenMaxHeight: s.screenMaxHeight,
      ScreenMaxFps: s.screenMaxFps,
      ScreenVideoMaxFps: s.screenVideoMaxFps,
      ScreenBitrateKbps: s.screenBitrateKbps,
      ScreenHighBitrateKbps: s.screenHighBitrateKbps,
      AudioMaxAverageBitrateKbps: s.audioMaxAverageBitrateKbps,
      MuteReleasesHardwareMinutes: s.muteReleasesHardwareMinutes,
      SoundsVolumePct: s.soundsVolumePct,
      SoundsRepeatGapMs: s.soundsRepeatGapMs,
      SfuMaxIncomingBitrateKbps: s.sfuMaxIncomingBitrateKbps,
      SfuStatsSampleSeconds: s.sfuStatsSampleSeconds,
      CameraMaxBitrateKbps: s.cameraMaxBitrateKbps,
      ClientRecordingAudioKbps: s.clientRecordingAudioKbps,
      OpusFecEnabled: s.opusFecEnabled,
      OpusDtxEnabled: s.opusDtxEnabled,
      AudioEchoCancellationDefault: s.audioEchoCancellationDefault,
      AudioNoiseSuppressionDefault: s.audioNoiseSuppressionDefault,
      AudioAutoGainControlDefault: s.audioAutoGainControlDefault,
      ClientsCanShareScreen: s.clientsCanShareScreen,
      ScreenAudioAllowed: s.screenAudioAllowed,
      EchoDetectionEnabled: s.echoDetectionEnabled,
      EchoMuteOnDetect: s.echoMuteOnDetect,
      ReactionsEnabled: s.reactionsEnabled,
      ReactionDurationSeconds: s.reactionDurationSeconds,
      ReactionsPerUserPer10s: s.reactionsPerUserPer10s,
      ReactionsPerMeetingPerMinute: s.reactionsPerMeetingPerMinute,
      MeetingReactionCatalog: JSON.stringify(s.meetingReactionCatalog),
      MeetingReminderMinutesBefore: s.meetingReminderMinutesBefore,
      GuestsWaitingRoomDefault: s.guestsWaitingRoomDefault,
      UpdatedByUserId: s.updatedByUserId,
    };
    await this.prisma.platformCommunicationSettings.upsert({
      where: { Id: GLOBAL_ID },
      create: { Id: GLOBAL_ID, ...data },
      update: data,
    });
  }
}

function toSnapshot(row: {
  Version: number;
  MaxMeetingDurationMinutes: number;
  DefaultMeetingDurationMinutes: number;
  DefaultMaxParticipants: number;
  MeshMaxParticipants: number;
  DisconnectGraceSeconds: number;
  ScheduledMeetingExpiryHours: number;
  EmptyMeetingEndMinutes: number;
  DominantSpeakerMaxPerSecond: number;
  GuestsAllowed: boolean;
  RingingTimeoutSeconds: number;
  RecordingConsentTimeoutMeetingSeconds: number;
  RecordingConsentTimeoutCallSeconds: number;
  MaxRecordingMinutesMeeting: number;
  MaxRecordingMinutesAudioCall: number;
  MaxRecordingMinutesVideoCall: number;
  InvitationMinTtlHours: number;
  InvitationExpiryGraceHours: number;
  InternalGroupsEnabled: boolean;
  MeetingChatAttachmentsPerMinute: number;
  MeetingChatAttachmentsPerMeeting: number;
  MaxReactionsPerMessage: number;
  MeetingSoundsPolicy: string;
  TranscriptDefaultLanguage: string;
  OutboxRetentionDays: number;
  ProcessedEventRetentionDays: number;
  IdempotencyRetentionDays: number;
  QualitySampleIntervalSeconds: number;
  QualityExcellentRttMs: number;
  QualityGoodRttMs: number;
  QualityUnstableRttMs: number;
  QualityExcellentLossPct: number;
  QualityGoodLossPct: number;
  QualityUnstableLossPct: number;
  QualityExcellentJitterMs: number;
  QualityGoodJitterMs: number;
  QualityUnstableJitterMs: number;
  QualityUpgradeSamples: number;
  QualityPoorSustainedSamples: number;
  QualityRecoverySamples: number;
  CameraMaxWidth: number;
  CameraMaxHeight: number;
  CameraMaxFps: number;
  MeshCameraMaxWidth: number;
  MeshCameraMaxHeight: number;
  MeshCameraMaxFps: number;
  ScreenMaxWidth: number;
  ScreenMaxHeight: number;
  ScreenMaxFps: number;
  ScreenVideoMaxFps: number;
  ScreenBitrateKbps: number;
  ScreenHighBitrateKbps: number;
  AudioMaxAverageBitrateKbps: number;
  MuteReleasesHardwareMinutes: number;
  SoundsVolumePct: number;
  SoundsRepeatGapMs: number;
  SfuMaxIncomingBitrateKbps: number;
  SfuStatsSampleSeconds: number;
  CameraMaxBitrateKbps: number;
  ClientRecordingAudioKbps: number;
  OpusFecEnabled: boolean;
  OpusDtxEnabled: boolean;
  AudioEchoCancellationDefault: boolean;
  AudioNoiseSuppressionDefault: boolean;
  AudioAutoGainControlDefault: boolean;
  ClientsCanShareScreen: boolean;
  ScreenAudioAllowed: boolean;
  EchoDetectionEnabled: boolean;
  EchoMuteOnDetect: boolean;
  ReactionsEnabled: boolean;
  ReactionDurationSeconds: number;
  ReactionsPerUserPer10s: number;
  ReactionsPerMeetingPerMinute: number;
  MeetingReactionCatalog: string;
  MeetingReminderMinutesBefore: number;
  GuestsWaitingRoomDefault: boolean;
  UpdatedByUserId: string | null;
  UpdatedAtUtc: Date;
}): PlatformCommunicationSettingsSnapshot {
  return {
    version: row.Version,
    maxMeetingDurationMinutes: row.MaxMeetingDurationMinutes,
    defaultMeetingDurationMinutes: row.DefaultMeetingDurationMinutes,
    defaultMaxParticipants: row.DefaultMaxParticipants,
    meshMaxParticipants: row.MeshMaxParticipants,
    disconnectGraceSeconds: row.DisconnectGraceSeconds,
    scheduledMeetingExpiryHours: row.ScheduledMeetingExpiryHours,
    emptyMeetingEndMinutes: row.EmptyMeetingEndMinutes,
    dominantSpeakerMaxPerSecond: row.DominantSpeakerMaxPerSecond,
    guestsAllowed: row.GuestsAllowed,
    ringingTimeoutSeconds: row.RingingTimeoutSeconds,
    recordingConsentTimeoutMeetingSeconds: row.RecordingConsentTimeoutMeetingSeconds,
    recordingConsentTimeoutCallSeconds: row.RecordingConsentTimeoutCallSeconds,
    maxRecordingMinutesMeeting: row.MaxRecordingMinutesMeeting,
    maxRecordingMinutesAudioCall: row.MaxRecordingMinutesAudioCall,
    maxRecordingMinutesVideoCall: row.MaxRecordingMinutesVideoCall,
    invitationMinTtlHours: row.InvitationMinTtlHours,
    invitationExpiryGraceHours: row.InvitationExpiryGraceHours,
    internalGroupsEnabled: row.InternalGroupsEnabled,
    meetingChatAttachmentsPerMinute: row.MeetingChatAttachmentsPerMinute,
    meetingChatAttachmentsPerMeeting: row.MeetingChatAttachmentsPerMeeting,
    maxReactionsPerMessage: row.MaxReactionsPerMessage,
    // rehydrate() valida el enum; aquí solo se tipa.
    meetingSoundsPolicy: row.MeetingSoundsPolicy as MeetingSoundsPolicy,
    transcriptDefaultLanguage: row.TranscriptDefaultLanguage,
    outboxRetentionDays: row.OutboxRetentionDays,
    processedEventRetentionDays: row.ProcessedEventRetentionDays,
    idempotencyRetentionDays: row.IdempotencyRetentionDays,
    qualitySampleIntervalSeconds: row.QualitySampleIntervalSeconds,
    qualityExcellentRttMs: row.QualityExcellentRttMs,
    qualityGoodRttMs: row.QualityGoodRttMs,
    qualityUnstableRttMs: row.QualityUnstableRttMs,
    qualityExcellentLossPct: row.QualityExcellentLossPct,
    qualityGoodLossPct: row.QualityGoodLossPct,
    qualityUnstableLossPct: row.QualityUnstableLossPct,
    qualityExcellentJitterMs: row.QualityExcellentJitterMs,
    qualityGoodJitterMs: row.QualityGoodJitterMs,
    qualityUnstableJitterMs: row.QualityUnstableJitterMs,
    qualityUpgradeSamples: row.QualityUpgradeSamples,
    qualityPoorSustainedSamples: row.QualityPoorSustainedSamples,
    qualityRecoverySamples: row.QualityRecoverySamples,
    cameraMaxWidth: row.CameraMaxWidth,
    cameraMaxHeight: row.CameraMaxHeight,
    cameraMaxFps: row.CameraMaxFps,
    meshCameraMaxWidth: row.MeshCameraMaxWidth,
    meshCameraMaxHeight: row.MeshCameraMaxHeight,
    meshCameraMaxFps: row.MeshCameraMaxFps,
    screenMaxWidth: row.ScreenMaxWidth,
    screenMaxHeight: row.ScreenMaxHeight,
    screenMaxFps: row.ScreenMaxFps,
    screenVideoMaxFps: row.ScreenVideoMaxFps,
    screenBitrateKbps: row.ScreenBitrateKbps,
    screenHighBitrateKbps: row.ScreenHighBitrateKbps,
    audioMaxAverageBitrateKbps: row.AudioMaxAverageBitrateKbps,
    muteReleasesHardwareMinutes: row.MuteReleasesHardwareMinutes,
    soundsVolumePct: row.SoundsVolumePct,
    soundsRepeatGapMs: row.SoundsRepeatGapMs,
    sfuMaxIncomingBitrateKbps: row.SfuMaxIncomingBitrateKbps,
    sfuStatsSampleSeconds: row.SfuStatsSampleSeconds,
    cameraMaxBitrateKbps: row.CameraMaxBitrateKbps,
    clientRecordingAudioKbps: row.ClientRecordingAudioKbps,
    opusFecEnabled: row.OpusFecEnabled,
    opusDtxEnabled: row.OpusDtxEnabled,
    audioEchoCancellationDefault: row.AudioEchoCancellationDefault,
    audioNoiseSuppressionDefault: row.AudioNoiseSuppressionDefault,
    audioAutoGainControlDefault: row.AudioAutoGainControlDefault,
    clientsCanShareScreen: row.ClientsCanShareScreen,
    screenAudioAllowed: row.ScreenAudioAllowed,
    echoDetectionEnabled: row.EchoDetectionEnabled,
    echoMuteOnDetect: row.EchoMuteOnDetect,
    reactionsEnabled: row.ReactionsEnabled,
    reactionDurationSeconds: row.ReactionDurationSeconds,
    reactionsPerUserPer10s: row.ReactionsPerUserPer10s,
    reactionsPerMeetingPerMinute: row.ReactionsPerMeetingPerMinute,
    // Invalido -> [] y rehydrate() lo repone al default dejando rastro.
    meetingReactionCatalog: parseReactionCatalogColumn(row.MeetingReactionCatalog) ?? [],
    meetingReminderMinutesBefore: row.MeetingReminderMinutesBefore,
    guestsWaitingRoomDefault: row.GuestsWaitingRoomDefault,
    updatedByUserId: row.UpdatedByUserId,
    updatedAtUtc: row.UpdatedAtUtc,
  };
}
