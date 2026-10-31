import { Result, makeError } from '../shared/result.js';
import { type ConfigurationChange, diffPatch } from './configuration-change.js';
import {
  DEFAULT_MEETING_REACTION_CATALOG,
  TECHNICAL_LIMITS,
  type MeetingSoundsPolicy,
  type TechnicalLimitKey,
  clampToTechnical,
  describeTechnicalRange,
  isMeetingSoundsPolicy,
  isReactionCatalog,
  isRecordingCompositionFormat,
  type RecordingCompositionFormat,
  isTranscriptLanguage,
  isWithinTechnical,
} from './technical-limits.js';

/**
 * Aggregate root: valores globales de la plataforma (fila única "global"). Solo PlatformAdmin
 * los cambia; cada cambio sube `version` para que las cachés por oficina se recomputen.
 */
export interface PlatformCommunicationSettingsSnapshot {
  readonly version: number;
  readonly maxMeetingDurationMinutes: number;
  readonly defaultMeetingDurationMinutes: number;
  readonly defaultMaxParticipants: number;
  readonly meshMaxParticipants: number;
  readonly disconnectGraceSeconds: number;
  readonly scheduledMeetingExpiryHours: number;
  readonly emptyMeetingEndMinutes: number;
  readonly dominantSpeakerMaxPerSecond: number;
  readonly guestsAllowed: boolean;
  readonly ringingTimeoutSeconds: number;
  readonly recordingConsentTimeoutMeetingSeconds: number;
  readonly recordingConsentTimeoutCallSeconds: number;
  readonly maxRecordingMinutesMeeting: number;
  readonly maxRecordingMinutesAudioCall: number;
  readonly maxRecordingMinutesVideoCall: number;
  readonly invitationMinTtlHours: number;
  readonly invitationExpiryGraceHours: number;
  readonly internalGroupsEnabled: boolean;
  readonly meetingChatAttachmentsPerMinute: number;
  readonly meetingChatAttachmentsPerMeeting: number;
  readonly maxReactionsPerMessage: number;
  // F8: ventana de edición por tipo de chat (techo de la plataforma) y tope de ediciones.
  readonly chatEditWindowMinutesDirect: number;
  readonly chatEditWindowMinutesGroup: number;
  readonly chatEditWindowMinutesSupport: number;
  readonly chatEditWindowMinutesMeeting: number;
  readonly chatMaxEditsPerMessage: number;
  readonly meetingSoundsPolicy: MeetingSoundsPolicy;
  readonly transcriptDefaultLanguage: string;
  readonly outboxRetentionDays: number;
  readonly processedEventRetentionDays: number;
  readonly idempotencyRetentionDays: number;
  // F4: politica de media (calidad, video, audio, pantalla, sonidos, eco, SFU).
  readonly qualitySampleIntervalSeconds: number;
  readonly qualityExcellentRttMs: number;
  readonly qualityGoodRttMs: number;
  readonly qualityUnstableRttMs: number;
  readonly qualityExcellentLossPct: number;
  readonly qualityGoodLossPct: number;
  readonly qualityUnstableLossPct: number;
  readonly qualityExcellentJitterMs: number;
  readonly qualityGoodJitterMs: number;
  readonly qualityUnstableJitterMs: number;
  readonly qualityUpgradeSamples: number;
  readonly qualityPoorSustainedSamples: number;
  readonly qualityRecoverySamples: number;
  readonly cameraMaxWidth: number;
  readonly cameraMaxHeight: number;
  readonly cameraMaxFps: number;
  readonly meshCameraMaxWidth: number;
  readonly meshCameraMaxHeight: number;
  readonly meshCameraMaxFps: number;
  readonly screenMaxWidth: number;
  readonly screenMaxHeight: number;
  readonly screenMaxFps: number;
  readonly screenVideoMaxFps: number;
  readonly screenBitrateKbps: number;
  readonly screenHighBitrateKbps: number;
  readonly audioMaxAverageBitrateKbps: number;
  readonly muteReleasesHardwareMinutes: number;
  readonly soundsVolumePct: number;
  readonly soundsRepeatGapMs: number;
  readonly sfuMaxIncomingBitrateKbps: number;
  readonly sfuStatsSampleSeconds: number;
  readonly cameraMaxBitrateKbps: number;
  readonly clientRecordingAudioKbps: number;
  readonly opusFecEnabled: boolean;
  readonly opusDtxEnabled: boolean;
  readonly audioEchoCancellationDefault: boolean;
  readonly audioNoiseSuppressionDefault: boolean;
  readonly audioAutoGainControlDefault: boolean;
  readonly clientsCanShareScreen: boolean;
  readonly screenAudioAllowed: boolean;
  readonly echoDetectionEnabled: boolean;
  readonly echoMuteOnDetect: boolean;
  // F5 (D-50): reacciones efimeras del meeting; la oficina puede apagarlas o acotar el catalogo.
  readonly reactionsEnabled: boolean;
  readonly reactionDurationSeconds: number;
  readonly reactionsPerUserPer10s: number;
  readonly reactionsPerMeetingPerMinute: number;
  readonly meetingReactionCatalog: readonly string[];
  // F6 (D-47/D-48): recordatorio T-N (0 = apagado) y sala de espera por defecto para guests.
  readonly meetingReminderMinutesBefore: number;
  readonly guestsWaitingRoomDefault: boolean;
  // F7: grabación server-side (false = mezcla en el cliente, C-03 transitorio), límites, composición y retención.
  readonly serverSideRecordingEnabled: boolean;
  readonly recordingLimitWarningMinutes: number;
  readonly recordingLimitToleranceSeconds: number;
  readonly recordingCompositionWidth: number;
  readonly recordingCompositionHeight: number;
  readonly recordingCompositionFps: number;
  readonly recordingCompositionVideoKbps: number;
  readonly recordingCompositionAudioKbps: number;
  readonly recordingCompositionFormat: RecordingCompositionFormat;
  readonly recordingCaptureSpatialLayer: number;
  readonly recordingTracksRetentionDays: number;
  readonly voiceNoteTranscriptEnabled: boolean;
  readonly voiceNoteTranscriptMaxSeconds: number;
  readonly transcriptWorkerTimeoutFactorPct: number;
  readonly recordingAvailabilityTimeoutMinutes: number;
  readonly updatedByUserId: string | null;
  readonly updatedAtUtc: Date;
}

export type PlatformCommunicationSettingsPatch = Partial<
  Omit<PlatformCommunicationSettingsSnapshot, 'version' | 'updatedByUserId' | 'updatedAtUtc'>
>;

type NumericKey = {
  [K in keyof PlatformCommunicationSettingsSnapshot]: PlatformCommunicationSettingsSnapshot[K] extends number ? K : never;
}[keyof PlatformCommunicationSettingsSnapshot];

/** Qué rango técnico gobierna cada clave numérica (varias claves comparten rango). */
export const PLATFORM_NUMERIC_RANGES: Record<Exclude<NumericKey, 'version'>, TechnicalLimitKey> = {
  maxMeetingDurationMinutes: 'maxMeetingDurationMinutes',
  defaultMeetingDurationMinutes: 'defaultMeetingDurationMinutes',
  defaultMaxParticipants: 'maxParticipants',
  meshMaxParticipants: 'meshMaxParticipants',
  disconnectGraceSeconds: 'disconnectGraceSeconds',
  scheduledMeetingExpiryHours: 'scheduledMeetingExpiryHours',
  emptyMeetingEndMinutes: 'emptyMeetingEndMinutes',
  dominantSpeakerMaxPerSecond: 'dominantSpeakerMaxPerSecond',
  ringingTimeoutSeconds: 'ringingTimeoutSeconds',
  recordingConsentTimeoutMeetingSeconds: 'recordingConsentTimeoutSeconds',
  recordingConsentTimeoutCallSeconds: 'recordingConsentTimeoutSeconds',
  maxRecordingMinutesMeeting: 'maxRecordingMinutes',
  maxRecordingMinutesAudioCall: 'maxRecordingMinutes',
  maxRecordingMinutesVideoCall: 'maxRecordingMinutes',
  invitationMinTtlHours: 'invitationMinTtlHours',
  invitationExpiryGraceHours: 'invitationExpiryGraceHours',
  meetingChatAttachmentsPerMinute: 'meetingChatAttachmentsPerMinute',
  meetingChatAttachmentsPerMeeting: 'meetingChatAttachmentsPerMeeting',
  maxReactionsPerMessage: 'maxReactionsPerMessage',
  chatEditWindowMinutesDirect: 'chatEditWindowMinutesDirect',
  chatEditWindowMinutesGroup: 'chatEditWindowMinutesGroup',
  chatEditWindowMinutesSupport: 'chatEditWindowMinutesSupport',
  chatEditWindowMinutesMeeting: 'chatEditWindowMinutesMeeting',
  chatMaxEditsPerMessage: 'chatMaxEditsPerMessage',
  outboxRetentionDays: 'outboxRetentionDays',
  processedEventRetentionDays: 'processedEventRetentionDays',
  idempotencyRetentionDays: 'idempotencyRetentionDays',
  qualitySampleIntervalSeconds: 'qualitySampleIntervalSeconds',
  qualityExcellentRttMs: 'qualityExcellentRttMs',
  qualityGoodRttMs: 'qualityGoodRttMs',
  qualityUnstableRttMs: 'qualityUnstableRttMs',
  qualityExcellentLossPct: 'qualityExcellentLossPct',
  qualityGoodLossPct: 'qualityGoodLossPct',
  qualityUnstableLossPct: 'qualityUnstableLossPct',
  qualityExcellentJitterMs: 'qualityExcellentJitterMs',
  qualityGoodJitterMs: 'qualityGoodJitterMs',
  qualityUnstableJitterMs: 'qualityUnstableJitterMs',
  qualityUpgradeSamples: 'qualityUpgradeSamples',
  qualityPoorSustainedSamples: 'qualityPoorSustainedSamples',
  qualityRecoverySamples: 'qualityRecoverySamples',
  cameraMaxWidth: 'cameraMaxWidth',
  cameraMaxHeight: 'cameraMaxHeight',
  cameraMaxFps: 'cameraMaxFps',
  meshCameraMaxWidth: 'meshCameraMaxWidth',
  meshCameraMaxHeight: 'meshCameraMaxHeight',
  meshCameraMaxFps: 'meshCameraMaxFps',
  screenMaxWidth: 'screenMaxWidth',
  screenMaxHeight: 'screenMaxHeight',
  screenMaxFps: 'screenMaxFps',
  screenVideoMaxFps: 'screenVideoMaxFps',
  screenBitrateKbps: 'screenBitrateKbps',
  screenHighBitrateKbps: 'screenHighBitrateKbps',
  audioMaxAverageBitrateKbps: 'audioMaxAverageBitrateKbps',
  muteReleasesHardwareMinutes: 'muteReleasesHardwareMinutes',
  soundsVolumePct: 'soundsVolumePct',
  soundsRepeatGapMs: 'soundsRepeatGapMs',
  sfuMaxIncomingBitrateKbps: 'sfuMaxIncomingBitrateKbps',
  sfuStatsSampleSeconds: 'sfuStatsSampleSeconds',
  cameraMaxBitrateKbps: 'cameraMaxBitrateKbps',
  clientRecordingAudioKbps: 'clientRecordingAudioKbps',
  reactionDurationSeconds: 'reactionDurationSeconds',
  reactionsPerUserPer10s: 'reactionsPerUserPer10s',
  reactionsPerMeetingPerMinute: 'reactionsPerMeetingPerMinute',
  meetingReminderMinutesBefore: 'meetingReminderMinutesBefore',
  recordingLimitWarningMinutes: 'recordingLimitWarningMinutes',
  recordingLimitToleranceSeconds: 'recordingLimitToleranceSeconds',
  recordingCompositionWidth: 'recordingCompositionWidth',
  recordingCompositionHeight: 'recordingCompositionHeight',
  recordingCompositionFps: 'recordingCompositionFps',
  recordingCompositionVideoKbps: 'recordingCompositionVideoKbps',
  recordingCompositionAudioKbps: 'recordingCompositionAudioKbps',
  recordingCaptureSpatialLayer: 'recordingCaptureSpatialLayer',
  recordingTracksRetentionDays: 'recordingTracksRetentionDays',
  voiceNoteTranscriptMaxSeconds: 'voiceNoteTranscriptMaxSeconds',
  transcriptWorkerTimeoutFactorPct: 'transcriptWorkerTimeoutFactorPct',
  recordingAvailabilityTimeoutMinutes: 'recordingAvailabilityTimeoutMinutes',
};

export class PlatformCommunicationSettings {
  private constructor(private state: PlatformCommunicationSettingsSnapshot) {}

  /** Defaults = D-37 (60 min, 10 participantes, umbral SFU 4, gracia 30 s, consentimiento 30/15 s, grupos on). */
  static defaults(now: Date = new Date()): PlatformCommunicationSettings {
    return new PlatformCommunicationSettings({
      version: 1,
      maxMeetingDurationMinutes: TECHNICAL_LIMITS.maxMeetingDurationMinutes.fallback,
      defaultMeetingDurationMinutes: TECHNICAL_LIMITS.defaultMeetingDurationMinutes.fallback,
      defaultMaxParticipants: TECHNICAL_LIMITS.maxParticipants.fallback,
      meshMaxParticipants: TECHNICAL_LIMITS.meshMaxParticipants.fallback,
      disconnectGraceSeconds: TECHNICAL_LIMITS.disconnectGraceSeconds.fallback,
      scheduledMeetingExpiryHours: TECHNICAL_LIMITS.scheduledMeetingExpiryHours.fallback,
      emptyMeetingEndMinutes: TECHNICAL_LIMITS.emptyMeetingEndMinutes.fallback,
      dominantSpeakerMaxPerSecond: TECHNICAL_LIMITS.dominantSpeakerMaxPerSecond.fallback,
      guestsAllowed: true,
      ringingTimeoutSeconds: TECHNICAL_LIMITS.ringingTimeoutSeconds.fallback,
      recordingConsentTimeoutMeetingSeconds: 30,
      recordingConsentTimeoutCallSeconds: 15,
      maxRecordingMinutesMeeting: TECHNICAL_LIMITS.maxRecordingMinutes.fallback,
      maxRecordingMinutesAudioCall: TECHNICAL_LIMITS.maxRecordingMinutes.fallback,
      maxRecordingMinutesVideoCall: TECHNICAL_LIMITS.maxRecordingMinutes.fallback,
      invitationMinTtlHours: TECHNICAL_LIMITS.invitationMinTtlHours.fallback,
      invitationExpiryGraceHours: TECHNICAL_LIMITS.invitationExpiryGraceHours.fallback,
      internalGroupsEnabled: true,
      meetingChatAttachmentsPerMinute: TECHNICAL_LIMITS.meetingChatAttachmentsPerMinute.fallback,
      meetingChatAttachmentsPerMeeting: TECHNICAL_LIMITS.meetingChatAttachmentsPerMeeting.fallback,
      maxReactionsPerMessage: TECHNICAL_LIMITS.maxReactionsPerMessage.fallback,
      chatEditWindowMinutesDirect: TECHNICAL_LIMITS.chatEditWindowMinutesDirect.fallback,
      chatEditWindowMinutesGroup: TECHNICAL_LIMITS.chatEditWindowMinutesGroup.fallback,
      chatEditWindowMinutesSupport: TECHNICAL_LIMITS.chatEditWindowMinutesSupport.fallback,
      chatEditWindowMinutesMeeting: TECHNICAL_LIMITS.chatEditWindowMinutesMeeting.fallback,
      chatMaxEditsPerMessage: TECHNICAL_LIMITS.chatMaxEditsPerMessage.fallback,
      meetingSoundsPolicy: 'all',
      transcriptDefaultLanguage: 'auto',
      outboxRetentionDays: TECHNICAL_LIMITS.outboxRetentionDays.fallback,
      processedEventRetentionDays: TECHNICAL_LIMITS.processedEventRetentionDays.fallback,
      idempotencyRetentionDays: TECHNICAL_LIMITS.idempotencyRetentionDays.fallback,
      qualitySampleIntervalSeconds: TECHNICAL_LIMITS.qualitySampleIntervalSeconds.fallback,
      qualityExcellentRttMs: TECHNICAL_LIMITS.qualityExcellentRttMs.fallback,
      qualityGoodRttMs: TECHNICAL_LIMITS.qualityGoodRttMs.fallback,
      qualityUnstableRttMs: TECHNICAL_LIMITS.qualityUnstableRttMs.fallback,
      qualityExcellentLossPct: TECHNICAL_LIMITS.qualityExcellentLossPct.fallback,
      qualityGoodLossPct: TECHNICAL_LIMITS.qualityGoodLossPct.fallback,
      qualityUnstableLossPct: TECHNICAL_LIMITS.qualityUnstableLossPct.fallback,
      qualityExcellentJitterMs: TECHNICAL_LIMITS.qualityExcellentJitterMs.fallback,
      qualityGoodJitterMs: TECHNICAL_LIMITS.qualityGoodJitterMs.fallback,
      qualityUnstableJitterMs: TECHNICAL_LIMITS.qualityUnstableJitterMs.fallback,
      qualityUpgradeSamples: TECHNICAL_LIMITS.qualityUpgradeSamples.fallback,
      qualityPoorSustainedSamples: TECHNICAL_LIMITS.qualityPoorSustainedSamples.fallback,
      qualityRecoverySamples: TECHNICAL_LIMITS.qualityRecoverySamples.fallback,
      cameraMaxWidth: TECHNICAL_LIMITS.cameraMaxWidth.fallback,
      cameraMaxHeight: TECHNICAL_LIMITS.cameraMaxHeight.fallback,
      cameraMaxFps: TECHNICAL_LIMITS.cameraMaxFps.fallback,
      meshCameraMaxWidth: TECHNICAL_LIMITS.meshCameraMaxWidth.fallback,
      meshCameraMaxHeight: TECHNICAL_LIMITS.meshCameraMaxHeight.fallback,
      meshCameraMaxFps: TECHNICAL_LIMITS.meshCameraMaxFps.fallback,
      screenMaxWidth: TECHNICAL_LIMITS.screenMaxWidth.fallback,
      screenMaxHeight: TECHNICAL_LIMITS.screenMaxHeight.fallback,
      screenMaxFps: TECHNICAL_LIMITS.screenMaxFps.fallback,
      screenVideoMaxFps: TECHNICAL_LIMITS.screenVideoMaxFps.fallback,
      screenBitrateKbps: TECHNICAL_LIMITS.screenBitrateKbps.fallback,
      screenHighBitrateKbps: TECHNICAL_LIMITS.screenHighBitrateKbps.fallback,
      audioMaxAverageBitrateKbps: TECHNICAL_LIMITS.audioMaxAverageBitrateKbps.fallback,
      muteReleasesHardwareMinutes: TECHNICAL_LIMITS.muteReleasesHardwareMinutes.fallback,
      soundsVolumePct: TECHNICAL_LIMITS.soundsVolumePct.fallback,
      soundsRepeatGapMs: TECHNICAL_LIMITS.soundsRepeatGapMs.fallback,
      sfuMaxIncomingBitrateKbps: TECHNICAL_LIMITS.sfuMaxIncomingBitrateKbps.fallback,
      sfuStatsSampleSeconds: TECHNICAL_LIMITS.sfuStatsSampleSeconds.fallback,
      cameraMaxBitrateKbps: TECHNICAL_LIMITS.cameraMaxBitrateKbps.fallback,
      clientRecordingAudioKbps: TECHNICAL_LIMITS.clientRecordingAudioKbps.fallback,
      opusFecEnabled: true,
      opusDtxEnabled: true,
      audioEchoCancellationDefault: true,
      audioNoiseSuppressionDefault: true,
      audioAutoGainControlDefault: true,
      clientsCanShareScreen: true,
      screenAudioAllowed: true,
      echoDetectionEnabled: true,
      echoMuteOnDetect: false,
      reactionsEnabled: true,
      reactionDurationSeconds: TECHNICAL_LIMITS.reactionDurationSeconds.fallback,
      reactionsPerUserPer10s: TECHNICAL_LIMITS.reactionsPerUserPer10s.fallback,
      reactionsPerMeetingPerMinute: TECHNICAL_LIMITS.reactionsPerMeetingPerMinute.fallback,
      meetingReactionCatalog: [...DEFAULT_MEETING_REACTION_CATALOG],
      meetingReminderMinutesBefore: TECHNICAL_LIMITS.meetingReminderMinutesBefore.fallback,
      guestsWaitingRoomDefault: true,
      serverSideRecordingEnabled: true,
      recordingLimitWarningMinutes: TECHNICAL_LIMITS.recordingLimitWarningMinutes.fallback,
      recordingLimitToleranceSeconds: TECHNICAL_LIMITS.recordingLimitToleranceSeconds.fallback,
      recordingCompositionWidth: TECHNICAL_LIMITS.recordingCompositionWidth.fallback,
      recordingCompositionHeight: TECHNICAL_LIMITS.recordingCompositionHeight.fallback,
      recordingCompositionFps: TECHNICAL_LIMITS.recordingCompositionFps.fallback,
      recordingCompositionVideoKbps: TECHNICAL_LIMITS.recordingCompositionVideoKbps.fallback,
      recordingCompositionAudioKbps: TECHNICAL_LIMITS.recordingCompositionAudioKbps.fallback,
      recordingCompositionFormat: 'mp4',
      recordingCaptureSpatialLayer: TECHNICAL_LIMITS.recordingCaptureSpatialLayer.fallback,
      recordingTracksRetentionDays: TECHNICAL_LIMITS.recordingTracksRetentionDays.fallback,
      voiceNoteTranscriptEnabled: true,
      voiceNoteTranscriptMaxSeconds: TECHNICAL_LIMITS.voiceNoteTranscriptMaxSeconds.fallback,
      transcriptWorkerTimeoutFactorPct: TECHNICAL_LIMITS.transcriptWorkerTimeoutFactorPct.fallback,
      recordingAvailabilityTimeoutMinutes: TECHNICAL_LIMITS.recordingAvailabilityTimeoutMinutes.fallback,
      updatedByUserId: null,
      updatedAtUtc: now,
    });
  }

  /**
   * Rehidrata con lectura fail-safe: cada número fuera de su rango técnico vuelve al fallback y
   * se devuelve en `corrected` para que infraestructura lo registre.
   */
  static rehydrate(snapshot: PlatformCommunicationSettingsSnapshot): {
    settings: PlatformCommunicationSettings;
    corrected: string[];
  } {
    const corrected: string[] = [];
    const next: Record<string, unknown> = { ...snapshot };
    for (const key of Object.keys(PLATFORM_NUMERIC_RANGES) as Array<keyof typeof PLATFORM_NUMERIC_RANGES>) {
      const rangeKey = PLATFORM_NUMERIC_RANGES[key];
      const value = snapshot[key];
      if (!isWithinTechnical(rangeKey, value)) {
        next[key] = TECHNICAL_LIMITS[rangeKey].fallback;
        corrected.push(key);
      }
    }
    if (!isMeetingSoundsPolicy(snapshot.meetingSoundsPolicy)) {
      next['meetingSoundsPolicy'] = 'all';
      corrected.push('meetingSoundsPolicy');
    }
    if (!isTranscriptLanguage(snapshot.transcriptDefaultLanguage)) {
      next['transcriptDefaultLanguage'] = 'auto';
      corrected.push('transcriptDefaultLanguage');
    }
    if (!isReactionCatalog(snapshot.meetingReactionCatalog)) {
      next['meetingReactionCatalog'] = [...DEFAULT_MEETING_REACTION_CATALOG];
      corrected.push('meetingReactionCatalog');
    }
    if (typeof snapshot.reactionsEnabled !== 'boolean') {
      next['reactionsEnabled'] = true;
      corrected.push('reactionsEnabled');
    }
    if (typeof snapshot.guestsWaitingRoomDefault !== 'boolean') {
      next['guestsWaitingRoomDefault'] = true;
      corrected.push('guestsWaitingRoomDefault');
    }
    for (const key of ['serverSideRecordingEnabled', 'voiceNoteTranscriptEnabled'] as const) {
      if (typeof snapshot[key] !== 'boolean') {
        next[key] = true;
        corrected.push(key);
      }
    }
    if (!isRecordingCompositionFormat(snapshot.recordingCompositionFormat)) {
      next['recordingCompositionFormat'] = 'mp4';
      corrected.push('recordingCompositionFormat');
    }
    return {
      settings: new PlatformCommunicationSettings(next as unknown as PlatformCommunicationSettingsSnapshot),
      corrected,
    };
  }

  /** Aplica un patch validado contra los techos técnicos. Devuelve los cambios reales (para auditar). */
  update(
    patch: PlatformCommunicationSettingsPatch,
    actor: { userId: string; now?: Date },
  ): Result<ConfigurationChange[]> {
    const validation = PlatformCommunicationSettings.validate(patch);
    if (!validation.isSuccess) return Result.fail(validation.error);

    const ordering = validateQualityOrdering({ ...this.state, ...patch });
    if (!ordering.isSuccess) return Result.fail(ordering.error);

    const changes = diffPatch(this.state, patch);
    if (changes.length === 0) return Result.ok([]);

    this.state = {
      ...this.state,
      ...patch,
      version: this.state.version + 1,
      updatedByUserId: actor.userId,
      updatedAtUtc: actor.now ?? new Date(),
    };
    return Result.ok(changes);
  }

  private static validate(patch: PlatformCommunicationSettingsPatch): Result<void> {
    for (const key of Object.keys(PLATFORM_NUMERIC_RANGES) as Array<keyof typeof PLATFORM_NUMERIC_RANGES>) {
      const value = patch[key];
      if (value === undefined) continue;
      const rangeKey = PLATFORM_NUMERIC_RANGES[key];
      if (!isWithinTechnical(rangeKey, value)) {
        return Result.fail(
          makeError(
            'Settings.OutOfTechnicalRange',
            `${key} must be an integer within ${describeTechnicalRange(rangeKey)}.`,
          ),
        );
      }
    }
    const maxDuration = patch.maxMeetingDurationMinutes;
    const defaultDuration = patch.defaultMeetingDurationMinutes;
    if (maxDuration !== undefined && defaultDuration !== undefined && defaultDuration > maxDuration) {
      return Result.fail(
        makeError('Settings.DefaultAboveMax', 'defaultMeetingDurationMinutes cannot exceed maxMeetingDurationMinutes.'),
      );
    }
    if (patch.meetingReactionCatalog !== undefined && !isReactionCatalog(patch.meetingReactionCatalog)) {
      return Result.fail(makeError('Settings.InvalidReactionCatalog', 'meetingReactionCatalog must be 1..30 distinct short emojis.'));
    }
    if (patch.meetingSoundsPolicy !== undefined && !isMeetingSoundsPolicy(patch.meetingSoundsPolicy)) {
      return Result.fail(makeError('Settings.InvalidSoundsPolicy', 'meetingSoundsPolicy must be all, hostOnly or off.'));
    }
    if (patch.transcriptDefaultLanguage !== undefined && !isTranscriptLanguage(patch.transcriptDefaultLanguage)) {
      return Result.fail(makeError('Settings.InvalidLanguage', 'transcriptDefaultLanguage must be "auto" or a language code.'));
    }
    if (patch.recordingCompositionFormat !== undefined && !isRecordingCompositionFormat(patch.recordingCompositionFormat)) {
      return Result.fail(makeError('Settings.InvalidCompositionFormat', 'recordingCompositionFormat must be mp4 or webm.'));
    }
    return Result.okVoid();
  }

  toSnapshot(): PlatformCommunicationSettingsSnapshot {
    return this.state;
  }

  get version(): number {
    return this.state.version;
  }
}

/** F4: los umbrales de calidad deben ir en orden (Excellent < Good < Unstable) por metrica. */
export function validateQualityOrdering(s: {
  qualityExcellentRttMs: number; qualityGoodRttMs: number; qualityUnstableRttMs: number;
  qualityExcellentLossPct: number; qualityGoodLossPct: number; qualityUnstableLossPct: number;
  qualityExcellentJitterMs: number; qualityGoodJitterMs: number; qualityUnstableJitterMs: number;
}): Result<void> {
  const ordered = (a: number, b: number, c: number) => a < b && b < c;
  if (
    !ordered(s.qualityExcellentRttMs, s.qualityGoodRttMs, s.qualityUnstableRttMs) ||
    !ordered(s.qualityExcellentLossPct, s.qualityGoodLossPct, s.qualityUnstableLossPct) ||
    !ordered(s.qualityExcellentJitterMs, s.qualityGoodJitterMs, s.qualityUnstableJitterMs)
  ) {
    return Result.fail(makeError('Settings.QualityThresholdsOrder', 'Quality thresholds must satisfy excellent < good < unstable for RTT, loss and jitter.'));
  }
  return Result.okVoid();
}

/** Valor seguro para un lector que no pudo cargar la fila (misma tabla de fallbacks). */
export function platformFallback<K extends keyof typeof PLATFORM_NUMERIC_RANGES>(key: K): number {
  return clampToTechnical(PLATFORM_NUMERIC_RANGES[key], undefined);
}
