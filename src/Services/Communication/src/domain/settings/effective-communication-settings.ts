import type { PlatformCommunicationSettingsSnapshot } from './platform-communication-settings.js';
import type { TenantCommunicationLimitsSnapshot } from './tenant-communication-limits.js';
import type { TenantCommunicationConstraintsSnapshot } from './tenant-communication-constraints.js';
import type { TenantCommunicationSettingsSnapshot } from './tenant-communication-settings.js';
import type { UserCommunicationPreferencesSnapshot } from './user-communication-preferences.js';
import { DEFAULT_MEETING_REACTION_CATALOG, type MeetingSoundsPolicy, type TechnicalLimitKey, clampToTechnical } from './technical-limits.js';

/**
 * Techos efectivos para una oficina: lo máximo que su Tenant Admin puede fijar.
 * = clamp(técnico, global, plan, techo de oficina). Función pura (T-42/T-43).
 */
export interface EffectiveCeilings {
  readonly maxParticipants: number;
  readonly maxMeetingDurationMinutes: number;
  readonly maxRecordingMinutesMeeting: number;
  readonly maxRecordingMinutesAudioCall: number;
  readonly maxRecordingMinutesVideoCall: number;
  readonly meetingChatAttachmentsPerMinute: number;
  readonly meetingChatAttachmentsPerMeeting: number;
  readonly recordingAllowed: boolean;
  readonly recordingVideoAllowed: boolean;
  readonly transcriptAllowed: boolean;
  readonly callsAllowed: boolean;
  readonly supportAllowed: boolean;
  readonly guestsAllowed: boolean;
}

/** Valores vigentes para una oficina (y, si se pide, un usuario). Nadie lee constantes: todo sale de aquí. */
export interface EffectiveCommunicationSettings {
  readonly tenantId: string;
  readonly platformVersion: number;
  readonly planCode: string | null;
  readonly isSuspended: boolean;
  readonly ceilings: EffectiveCeilings;
  readonly meeting: {
    readonly enabled: boolean;
    readonly maxDurationMinutes: number;
    readonly defaultDurationMinutes: number;
    readonly maxParticipants: number;
    readonly meshMaxParticipants: number;
    readonly disconnectGraceSeconds: number;
    readonly scheduledExpiryHours: number;
    readonly emptyEndMinutes: number;
    readonly dominantSpeakerMaxPerSecond: number;
    readonly guestsAllowed: boolean;
    /** F6: minutos antes del inicio para el recordatorio (0 = apagado) y sala de espera forzada para guests. */
    readonly reminderMinutesBefore: number;
    readonly guestsWaitingRoom: boolean;
    readonly soundsPolicy: MeetingSoundsPolicy;
    readonly chatAttachmentsPerMinute: number;
    readonly chatAttachmentsPerMeeting: number;
  };
  readonly calls: {
    readonly enabled: boolean;
    readonly videoEnabled: boolean;
    readonly ringingTimeoutSeconds: number;
    readonly maxCallMinutes: number | null;
    readonly maxConcurrentCalls: number | null;
    readonly maxMonthlyMinutes: number | null;
  };
  readonly recording: {
    readonly enabled: boolean;
    readonly videoEnabled: boolean;
    readonly transcriptEnabled: boolean;
    readonly maxMinutesMeeting: number;
    readonly maxMinutesAudioCall: number;
    readonly maxMinutesVideoCall: number;
    readonly consentTimeoutMeetingSeconds: number;
    readonly consentTimeoutCallSeconds: number;
    readonly visibleInPortal: boolean;
    readonly transcriptLanguage: string;
  };
  readonly chat: {
    readonly enabled: boolean;
    readonly internalGroupsEnabled: boolean;
    readonly employeeToEmployeeChatEnabled: boolean;
    readonly maxReactionsPerMessage: number;
  };
  readonly support: { readonly enabled: boolean };
  readonly invitations: {
    readonly minTtlHours: number;
    readonly expiryGraceHours: number;
  };
  /** F4: politica de media (plataforma > oficina). Lo que leen los fronts via GET /communication/media-policy. */
  readonly media: EffectiveMediaPolicy;
  /** Solo cuando se resolvió para un usuario concreto. */
  readonly user: {
    readonly soundsMuted: boolean;
    readonly transcriptLanguage: string;
    readonly preferAudioOnly: boolean;
    readonly joinMuted: boolean;
    readonly joinCameraOff: boolean;
    /** null = default de la oficina (ya aplicado en `media.audio.processing`). */
    readonly audioProcessing: {
      readonly echoCancellation: boolean | null;
      readonly noiseSuppression: boolean | null;
      readonly autoGainControl: boolean | null;
    };
  } | null;
}

export interface QualityThreshold {
  readonly rttMs: number;
  readonly lossPct: number;
  readonly jitterMs: number;
}

export interface VideoProfile {
  readonly maxWidth: number;
  readonly maxHeight: number;
  readonly maxFps: number;
}

export interface AudioProcessingFlags {
  readonly echoCancellation: boolean;
  readonly noiseSuppression: boolean;
  readonly autoGainControl: boolean;
}

export interface EffectiveMediaPolicy {
  readonly quality: {
    readonly sampleIntervalSeconds: number;
    readonly thresholds: { readonly excellent: QualityThreshold; readonly good: QualityThreshold; readonly unstable: QualityThreshold };
    readonly upgradeSamples: number;
    readonly poorSustainedSamples: number;
    readonly recoverySamples: number;
  };
  readonly video: {
    readonly camera: VideoProfile & { readonly maxBitrateKbps: number };
    readonly meshCamera: VideoProfile;
    readonly screen: VideoProfile & { readonly videoFps: number; readonly bitrateKbps: number; readonly highBitrateKbps: number };
  };
  readonly audio: {
    readonly opusFec: boolean;
    readonly opusDtx: boolean;
    readonly maxAverageBitrateKbps: number;
    /** C-03: mezcla local de grabacion en el cliente mientras no exista la server-side. */
    readonly clientRecordingKbps: number;
    /** Defaults de la oficina (plataforma salvo override de oficina); el usuario puede pisarlos (ver `user`). */
    readonly processing: AudioProcessingFlags;
    readonly muteReleasesHardwareMinutes: number;
  };
  readonly screenShare: { readonly clientsCanShare: boolean; readonly audioAllowed: boolean };
  readonly sounds: { readonly volumePct: number; readonly repeatGapMs: number };
  readonly echo: { readonly detectionEnabled: boolean; readonly muteOnEcho: boolean };
  readonly sfu: { readonly maxIncomingBitrateKbps: number; readonly statsSampleSeconds: number };
  /** F5 (D-50): reacciones efimeras; `perMeetingPerMinute` solo lo aplica el servidor. */
  readonly reactions: {
    readonly enabled: boolean;
    readonly catalog: readonly string[];
    readonly durationSeconds: number;
    readonly perUserPer10s: number;
    readonly perMeetingPerMinute: number;
  };
  /** F5 (T-69): tope de `meeting.speaking` por segundo y usuario. */
  readonly speaking: { readonly maxPerSecond: number };
}

/** Seccion `user` a partir de las preferencias (null = sin fila: defaults). Un solo sitio para ambos resolvers. */
export function resolveUserSection(
  preferences: UserCommunicationPreferencesSnapshot | null,
  tenantLanguage: string,
): NonNullable<EffectiveCommunicationSettings['user']> {
  return {
    soundsMuted: preferences?.meetingSoundsMuted ?? false,
    transcriptLanguage: preferences?.transcriptLanguage ?? tenantLanguage,
    preferAudioOnly: preferences?.preferAudioOnly ?? false,
    joinMuted: preferences?.joinMuted ?? false,
    joinCameraOff: preferences?.joinCameraOff ?? false,
    audioProcessing: {
      echoCancellation: preferences?.echoCancellation ?? null,
      noiseSuppression: preferences?.noiseSuppression ?? null,
      autoGainControl: preferences?.autoGainControl ?? null,
    },
  };
}

/** Politica de media efectiva: numeros acotados al rango tecnico y booleanos oficina > plataforma. */
export function resolveMediaPolicy(
  platform: PlatformCommunicationSettingsSnapshot,
  tenant: TenantCommunicationSettingsSnapshot,
): EffectiveMediaPolicy {
  const n = (key: TechnicalLimitKey & keyof PlatformCommunicationSettingsSnapshot) =>
    clampToTechnical(key, platform[key] as number);
  return {
    quality: {
      sampleIntervalSeconds: n('qualitySampleIntervalSeconds'),
      thresholds: {
        excellent: { rttMs: n('qualityExcellentRttMs'), lossPct: n('qualityExcellentLossPct'), jitterMs: n('qualityExcellentJitterMs') },
        good: { rttMs: n('qualityGoodRttMs'), lossPct: n('qualityGoodLossPct'), jitterMs: n('qualityGoodJitterMs') },
        unstable: { rttMs: n('qualityUnstableRttMs'), lossPct: n('qualityUnstableLossPct'), jitterMs: n('qualityUnstableJitterMs') },
      },
      upgradeSamples: n('qualityUpgradeSamples'),
      poorSustainedSamples: n('qualityPoorSustainedSamples'),
      recoverySamples: n('qualityRecoverySamples'),
    },
    video: {
      camera: { maxWidth: n('cameraMaxWidth'), maxHeight: n('cameraMaxHeight'), maxFps: n('cameraMaxFps'), maxBitrateKbps: n('cameraMaxBitrateKbps') },
      meshCamera: { maxWidth: n('meshCameraMaxWidth'), maxHeight: n('meshCameraMaxHeight'), maxFps: n('meshCameraMaxFps') },
      screen: {
        maxWidth: n('screenMaxWidth'),
        maxHeight: n('screenMaxHeight'),
        maxFps: n('screenMaxFps'),
        videoFps: n('screenVideoMaxFps'),
        bitrateKbps: n('screenBitrateKbps'),
        highBitrateKbps: n('screenHighBitrateKbps'),
      },
    },
    audio: {
      opusFec: platform.opusFecEnabled,
      opusDtx: platform.opusDtxEnabled,
      maxAverageBitrateKbps: n('audioMaxAverageBitrateKbps'),
      clientRecordingKbps: n('clientRecordingAudioKbps'),
      processing: {
        echoCancellation: tenant.audioEchoCancellationDefault ?? platform.audioEchoCancellationDefault,
        noiseSuppression: tenant.audioNoiseSuppressionDefault ?? platform.audioNoiseSuppressionDefault,
        autoGainControl: tenant.audioAutoGainControlDefault ?? platform.audioAutoGainControlDefault,
      },
      muteReleasesHardwareMinutes: n('muteReleasesHardwareMinutes'),
    },
    screenShare: {
      // La oficina solo puede restringir lo que la plataforma permite.
      clientsCanShare: platform.clientsCanShareScreen && (tenant.clientsCanShareScreen ?? true),
      audioAllowed: platform.screenAudioAllowed && (tenant.screenAudioAllowed ?? true),
    },
    sounds: { volumePct: n('soundsVolumePct'), repeatGapMs: n('soundsRepeatGapMs') },
    echo: { detectionEnabled: platform.echoDetectionEnabled, muteOnEcho: tenant.echoMuteOnDetect ?? platform.echoMuteOnDetect },
    sfu: { maxIncomingBitrateKbps: n('sfuMaxIncomingBitrateKbps'), statsSampleSeconds: n('sfuStatsSampleSeconds') },
    reactions: {
      // `??` por la cache de plataforma anterior al despliegue de F5 (snapshot sin estos campos).
      enabled: (platform.reactionsEnabled ?? true) && (tenant.reactionsEnabled ?? true),
      catalog: tenant.meetingReactionCatalog ?? platform.meetingReactionCatalog ?? DEFAULT_MEETING_REACTION_CATALOG,
      durationSeconds: n('reactionDurationSeconds'),
      perUserPer10s: n('reactionsPerUserPer10s'),
      perMeetingPerMinute: n('reactionsPerMeetingPerMinute'),
    },
    speaking: { maxPerSecond: n('dominantSpeakerMaxPerSecond') },
  };
}

export interface ResolveEffectiveInput {
  readonly platform: PlatformCommunicationSettingsSnapshot;
  readonly limits: TenantCommunicationLimitsSnapshot | null;
  readonly constraints: TenantCommunicationConstraintsSnapshot | null;
  readonly tenant: TenantCommunicationSettingsSnapshot;
  readonly preferences?: UserCommunicationPreferencesSnapshot | null;
}

/** min() ignorando null/undefined; el global siempre está. */
function lowest(global: number, ...others: Array<number | null | undefined>): number {
  let result = global;
  for (const value of others) {
    if (value !== null && value !== undefined && value < result) result = value;
  }
  return result;
}

/** Valor de oficina acotado al techo; null = el techo mismo (o el default dado). */
function officeValue(office: number | null, ceiling: number, fallback: number, range: TechnicalLimitKey): number {
  const chosen = office ?? fallback;
  return clampToTechnical(range, Math.min(chosen, ceiling));
}

export function resolveCeilings(input: {
  platform: PlatformCommunicationSettingsSnapshot;
  limits: TenantCommunicationLimitsSnapshot | null;
  constraints: TenantCommunicationConstraintsSnapshot | null;
}): EffectiveCeilings {
  const { platform, limits, constraints } = input;
  return {
    maxParticipants: clampToTechnical(
      'maxParticipants',
      lowest(platform.defaultMaxParticipants, limits?.maxMeetingParticipants, constraints?.maxMeetingParticipants),
    ),
    maxMeetingDurationMinutes: clampToTechnical(
      'maxMeetingDurationMinutes',
      lowest(platform.maxMeetingDurationMinutes, limits?.maxMeetingMinutes, constraints?.maxMeetingDurationMinutes),
    ),
    maxRecordingMinutesMeeting: clampToTechnical(
      'maxRecordingMinutes',
      lowest(
        platform.maxRecordingMinutesMeeting,
        limits?.maxRecordingMinutesMeeting,
        constraints?.maxRecordingMinutesMeeting,
        // Una grabación de reunión nunca dura más que la reunión (C-04).
        lowest(platform.maxMeetingDurationMinutes, limits?.maxMeetingMinutes, constraints?.maxMeetingDurationMinutes),
      ),
    ),
    maxRecordingMinutesAudioCall: clampToTechnical(
      'maxRecordingMinutes',
      lowest(platform.maxRecordingMinutesAudioCall, limits?.maxRecordingMinutesAudioCall, constraints?.maxRecordingMinutesAudioCall),
    ),
    maxRecordingMinutesVideoCall: clampToTechnical(
      'maxRecordingMinutes',
      lowest(platform.maxRecordingMinutesVideoCall, limits?.maxRecordingMinutesVideoCall, constraints?.maxRecordingMinutesVideoCall),
    ),
    meetingChatAttachmentsPerMinute: clampToTechnical('meetingChatAttachmentsPerMinute', platform.meetingChatAttachmentsPerMinute),
    meetingChatAttachmentsPerMeeting: clampToTechnical('meetingChatAttachmentsPerMeeting', platform.meetingChatAttachmentsPerMeeting),
    // Un plan que no define la clave no restringe (rige la plataforma): mismo criterio que los números.
    recordingAllowed: (limits?.recordingEnabled ?? true) && (constraints?.recordingAllowed ?? true),
    recordingVideoAllowed: (limits?.recordingEnabled ?? true) && (limits?.recordingVideoEnabled ?? true) && (constraints?.recordingAllowed ?? true),
    transcriptAllowed: limits?.transcriptEnabled ?? true,
    callsAllowed: limits?.callsEnabled ?? true,
    supportAllowed: limits?.supportEnabled ?? true,
    guestsAllowed: platform.guestsAllowed && (constraints?.guestsAllowed ?? true),
  };
}

export function resolveEffectiveSettings(input: ResolveEffectiveInput): EffectiveCommunicationSettings {
  const { platform, limits, constraints, tenant } = input;
  const preferences = input.preferences ?? null;
  const ceilings = resolveCeilings({ platform, limits, constraints });

  const maxDuration = officeValue(
    tenant.maxMeetingDurationMinutes,
    ceilings.maxMeetingDurationMinutes,
    ceilings.maxMeetingDurationMinutes,
    'maxMeetingDurationMinutes',
  );
  const defaultDuration = officeValue(
    tenant.defaultMeetingDurationMinutes,
    maxDuration,
    Math.min(platform.defaultMeetingDurationMinutes, maxDuration),
    'defaultMeetingDurationMinutes',
  );
  const soundsPolicy = tenant.meetingSoundsPolicy ?? platform.meetingSoundsPolicy;
  const tenantLanguage = tenant.transcriptLanguage ?? platform.transcriptDefaultLanguage;

  return {
    tenantId: tenant.tenantId,
    platformVersion: platform.version,
    planCode: limits?.planCode ?? null,
    isSuspended: limits?.isSuspended ?? false,
    ceilings,
    meeting: {
      enabled: tenant.meetingsEnabled,
      maxDurationMinutes: maxDuration,
      defaultDurationMinutes: defaultDuration,
      maxParticipants: officeValue(tenant.maxMeetingParticipants, ceilings.maxParticipants, ceilings.maxParticipants, 'maxParticipants'),
      meshMaxParticipants: clampToTechnical('meshMaxParticipants', platform.meshMaxParticipants),
      disconnectGraceSeconds: clampToTechnical('disconnectGraceSeconds', platform.disconnectGraceSeconds),
      scheduledExpiryHours: clampToTechnical('scheduledMeetingExpiryHours', platform.scheduledMeetingExpiryHours),
      emptyEndMinutes: clampToTechnical('emptyMeetingEndMinutes', platform.emptyMeetingEndMinutes),
      dominantSpeakerMaxPerSecond: clampToTechnical('dominantSpeakerMaxPerSecond', platform.dominantSpeakerMaxPerSecond),
      guestsAllowed: ceilings.guestsAllowed && tenant.guestsAllowed,
      reminderMinutesBefore: clampToTechnical('meetingReminderMinutesBefore', tenant.meetingReminderMinutesBefore ?? platform.meetingReminderMinutesBefore),
      // `??` por la cache de plataforma anterior al despliegue de F6.
      guestsWaitingRoom: tenant.guestsWaitingRoom ?? platform.guestsWaitingRoomDefault ?? true,
      soundsPolicy,
      chatAttachmentsPerMinute: officeValue(
        tenant.meetingChatAttachmentsPerMinute,
        ceilings.meetingChatAttachmentsPerMinute,
        ceilings.meetingChatAttachmentsPerMinute,
        'meetingChatAttachmentsPerMinute',
      ),
      chatAttachmentsPerMeeting: officeValue(
        tenant.meetingChatAttachmentsPerMeeting,
        ceilings.meetingChatAttachmentsPerMeeting,
        ceilings.meetingChatAttachmentsPerMeeting,
        'meetingChatAttachmentsPerMeeting',
      ),
    },
    calls: {
      enabled: ceilings.callsAllowed && tenant.callsEnabled,
      videoEnabled: ceilings.callsAllowed && tenant.callsEnabled && tenant.videoCallsEnabled,
      ringingTimeoutSeconds: clampToTechnical('ringingTimeoutSeconds', tenant.ringingTimeoutSeconds ?? platform.ringingTimeoutSeconds),
      maxCallMinutes: limits?.maxCallMinutes ?? null,
      maxConcurrentCalls: limits?.maxConcurrentCalls ?? null,
      maxMonthlyMinutes: limits?.maxMonthlyMinutes ?? null,
    },
    recording: {
      enabled: ceilings.recordingAllowed,
      videoEnabled: ceilings.recordingVideoAllowed,
      transcriptEnabled: ceilings.transcriptAllowed,
      maxMinutesMeeting: officeValue(
        tenant.maxRecordingMinutesMeeting,
        ceilings.maxRecordingMinutesMeeting,
        ceilings.maxRecordingMinutesMeeting,
        'maxRecordingMinutes',
      ),
      maxMinutesAudioCall: officeValue(
        tenant.maxRecordingMinutesAudioCall,
        ceilings.maxRecordingMinutesAudioCall,
        ceilings.maxRecordingMinutesAudioCall,
        'maxRecordingMinutes',
      ),
      maxMinutesVideoCall: officeValue(
        tenant.maxRecordingMinutesVideoCall,
        ceilings.maxRecordingMinutesVideoCall,
        ceilings.maxRecordingMinutesVideoCall,
        'maxRecordingMinutes',
      ),
      consentTimeoutMeetingSeconds: clampToTechnical('recordingConsentTimeoutSeconds', platform.recordingConsentTimeoutMeetingSeconds),
      consentTimeoutCallSeconds: clampToTechnical('recordingConsentTimeoutSeconds', platform.recordingConsentTimeoutCallSeconds),
      visibleInPortal: tenant.recordingVisibleInPortal,
      transcriptLanguage: tenantLanguage,
    },
    chat: {
      enabled: tenant.chatEnabled,
      // D-16: interruptor global; la oficina ya no lo decide.
      internalGroupsEnabled: platform.internalGroupsEnabled,
      employeeToEmployeeChatEnabled: tenant.employeeToEmployeeChatEnabled,
      maxReactionsPerMessage: clampToTechnical('maxReactionsPerMessage', platform.maxReactionsPerMessage),
    },
    support: { enabled: ceilings.supportAllowed && tenant.supportEnabled },
    invitations: {
      minTtlHours: clampToTechnical('invitationMinTtlHours', platform.invitationMinTtlHours),
      expiryGraceHours: clampToTechnical('invitationExpiryGraceHours', platform.invitationExpiryGraceHours),
    },
    media: resolveMediaPolicy(platform, tenant),
    user: preferences ? resolveUserSection(preferences, tenantLanguage) : null,
  };
}
