import { Result, makeError } from '../shared/result.js';
import { RecordingConsentPolicy, isRecordingConsentPolicy } from '../recording/recording-consent.js';
import { type ConfigurationChange, diffPatch } from './configuration-change.js';
import {
  type MeetingSoundsPolicy,
  type TechnicalLimitKey,
  describeTechnicalRange,
  isMeetingSoundsPolicy,
  isReactionCatalog,
  isTranscriptLanguage,
  isWithinTechnical,
} from './technical-limits.js';
import type { EffectiveCeilings } from './effective-communication-settings.js';

/**
 * Aggregate root: TenantCommunicationSettings. Un aggregate por tenant. Se
 * crea perezosamente al primer TenantCreated o al primer GET; para simplicidad
 * el use-case `getOrCreate` construye defaults conservadores.
 *
 * Los campos numericos nuevos son null cuando la oficina no fija nada (hereda el techo
 * efectivo). Nunca superan el techo: `validateAgainstCeilings` lo rechaza y
 * `applyCeilings` auto-corrige cuando PlatformAdmin baja un techo (ADD-09 H-4).
 * `internalGroupsEnabled` dejo de ser de la oficina: es interruptor global (D-16).
 */
export interface TenantCommunicationSettingsSnapshot {
  readonly tenantId: string;
  readonly chatEnabled: boolean;
  readonly callsEnabled: boolean;
  readonly videoCallsEnabled: boolean;
  readonly meetingsEnabled: boolean;
  readonly supportEnabled: boolean;
  readonly screenshotsEnabled: boolean;
  readonly employeeToEmployeeChatEnabled: boolean;
  /**
   * Fase B5 (chat tipado) — si esta en true, un chat directo que involucra a
   * un customer solo se permite si el otro lado es su preparador asignado
   * (CustomerPreparerAssignment). Default false, opt-in por tenant.
   */
  readonly restrictCustomerChatToAssignedPreparer: boolean;
  readonly defaultCameraOff: boolean;
  readonly defaultMicrophoneOff: boolean;
  readonly persistChatOnEnd: boolean;
  readonly messageRetentionDays: number;
  readonly recordingRetentionDays: number;
  readonly purgeEnabled: boolean;
  /**
   * Gap cerrado en Fase Backend 3 — la columna Prisma existia desde Fase 2
   * (default 'NoRejections') pero nunca se conecto al aggregate. Ver
   * evaluateRecordingConsentPolicy en domain/recording/recording-consent.ts.
   */
  readonly recordingConsentPolicy: RecordingConsentPolicy;
  readonly defaultMeetingDurationMinutes: number | null;
  readonly maxMeetingDurationMinutes: number | null;
  readonly maxMeetingParticipants: number | null;
  readonly maxRecordingMinutesMeeting: number | null;
  readonly maxRecordingMinutesAudioCall: number | null;
  readonly maxRecordingMinutesVideoCall: number | null;
  readonly ringingTimeoutSeconds: number | null;
  readonly meetingChatAttachmentsPerMinute: number | null;
  readonly meetingChatAttachmentsPerMeeting: number | null;
  readonly meetingSoundsPolicy: MeetingSoundsPolicy | null;
  readonly transcriptLanguage: string | null;
  readonly recordingVisibleInPortal: boolean;
  readonly guestsAllowed: boolean;
  // F4 (null = hereda la plataforma): pantalla D-42, procesamiento de audio D-46, eco D-52.
  readonly clientsCanShareScreen: boolean | null;
  readonly screenAudioAllowed: boolean | null;
  readonly audioEchoCancellationDefault: boolean | null;
  readonly audioNoiseSuppressionDefault: boolean | null;
  readonly audioAutoGainControlDefault: boolean | null;
  readonly echoMuteOnDetect: boolean | null;
  // F5 (D-50; null = hereda la plataforma): la oficina puede apagar reacciones o acotar el catalogo.
  readonly reactionsEnabled: boolean | null;
  readonly meetingReactionCatalog: readonly string[] | null;
  // F6 (null = hereda): recordatorio T-N y sala de espera para guests.
  readonly meetingReminderMinutesBefore: number | null;
  readonly guestsWaitingRoom: boolean | null;
  // F7: vocabulario de la oficina para el transcript (T-30) y notas de voz transcritas (null = hereda).
  readonly transcriptPrompt: string | null;
  readonly voiceNoteTranscriptEnabled: boolean | null;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
}

export type TenantCommunicationSettingsPatch = Partial<
  Omit<TenantCommunicationSettingsSnapshot, 'tenantId' | 'createdAtUtc' | 'updatedAtUtc'>
>;

type OfficeNumericKey =
  | 'defaultMeetingDurationMinutes'
  | 'maxMeetingDurationMinutes'
  | 'maxMeetingParticipants'
  | 'maxRecordingMinutesMeeting'
  | 'maxRecordingMinutesAudioCall'
  | 'maxRecordingMinutesVideoCall'
  | 'ringingTimeoutSeconds'
  | 'meetingChatAttachmentsPerMinute'
  | 'meetingChatAttachmentsPerMeeting'
  | 'meetingReminderMinutesBefore';

/** Claves numericas opcionales de la oficina y el rango tecnico que las acota. */
const OFFICE_NUMERIC_RANGES: Record<OfficeNumericKey, TechnicalLimitKey> = {
  defaultMeetingDurationMinutes: 'defaultMeetingDurationMinutes',
  maxMeetingDurationMinutes: 'maxMeetingDurationMinutes',
  maxMeetingParticipants: 'maxParticipants',
  maxRecordingMinutesMeeting: 'maxRecordingMinutes',
  maxRecordingMinutesAudioCall: 'maxRecordingMinutes',
  maxRecordingMinutesVideoCall: 'maxRecordingMinutes',
  ringingTimeoutSeconds: 'ringingTimeoutSeconds',
  meetingChatAttachmentsPerMinute: 'meetingChatAttachmentsPerMinute',
  meetingChatAttachmentsPerMeeting: 'meetingChatAttachmentsPerMeeting',
  meetingReminderMinutesBefore: 'meetingReminderMinutesBefore',
};

type CeilingedKey = Exclude<OfficeNumericKey, 'ringingTimeoutSeconds' | 'meetingReminderMinutesBefore'>;
type NumericCeilingKey = {
  [K in keyof EffectiveCeilings]: EffectiveCeilings[K] extends number ? K : never;
}[keyof EffectiveCeilings];

/** Clave de oficina → techo efectivo que no puede superar. */
const CEILING_OF: Record<CeilingedKey, NumericCeilingKey> = {
  defaultMeetingDurationMinutes: 'maxMeetingDurationMinutes',
  maxMeetingDurationMinutes: 'maxMeetingDurationMinutes',
  maxMeetingParticipants: 'maxParticipants',
  maxRecordingMinutesMeeting: 'maxRecordingMinutesMeeting',
  maxRecordingMinutesAudioCall: 'maxRecordingMinutesAudioCall',
  maxRecordingMinutesVideoCall: 'maxRecordingMinutesVideoCall',
  meetingChatAttachmentsPerMinute: 'meetingChatAttachmentsPerMinute',
  meetingChatAttachmentsPerMeeting: 'meetingChatAttachmentsPerMeeting',
};

/** whisper.cpp toma el prompt como contexto inicial: corto, solo nombres y vocabulario. */
export const TRANSCRIPT_PROMPT_MAX_LENGTH = 500;

export class TenantCommunicationSettings {
  private constructor(private state: TenantCommunicationSettingsSnapshot) {}

  static rehydrate(snapshot: TenantCommunicationSettingsSnapshot): TenantCommunicationSettings {
    return new TenantCommunicationSettings(snapshot);
  }

  static defaults(tenantId: string, now: Date = new Date()): TenantCommunicationSettings {
    return new TenantCommunicationSettings({
      tenantId,
      chatEnabled: true,
      callsEnabled: true,
      videoCallsEnabled: true,
      meetingsEnabled: true,
      supportEnabled: true,
      screenshotsEnabled: true,
      employeeToEmployeeChatEnabled: false,
      restrictCustomerChatToAssignedPreparer: false,
      defaultCameraOff: true,
      defaultMicrophoneOff: false,
      persistChatOnEnd: false,
      messageRetentionDays: 365,
      recordingRetentionDays: 90,
      purgeEnabled: false,
      recordingConsentPolicy: RecordingConsentPolicy.NoRejections,
      defaultMeetingDurationMinutes: null,
      maxMeetingDurationMinutes: null,
      maxMeetingParticipants: null,
      maxRecordingMinutesMeeting: null,
      maxRecordingMinutesAudioCall: null,
      maxRecordingMinutesVideoCall: null,
      ringingTimeoutSeconds: null,
      meetingChatAttachmentsPerMinute: null,
      meetingChatAttachmentsPerMeeting: null,
      meetingSoundsPolicy: null,
      transcriptLanguage: null,
      recordingVisibleInPortal: true,
      guestsAllowed: true,
      clientsCanShareScreen: null,
      screenAudioAllowed: null,
      audioEchoCancellationDefault: null,
      audioNoiseSuppressionDefault: null,
      audioAutoGainControlDefault: null,
      echoMuteOnDetect: null,
      reactionsEnabled: null,
      meetingReactionCatalog: null,
      meetingReminderMinutesBefore: null,
      guestsWaitingRoom: null,
      transcriptPrompt: null,
      voiceNoteTranscriptEnabled: null,
      createdAtUtc: now,
      updatedAtUtc: now,
    });
  }

  /** Validacion de forma (rangos tecnicos). La validacion contra el techo es `validateAgainstCeilings`. */
  update(patch: TenantCommunicationSettingsPatch, now: Date = new Date()): Result<ConfigurationChange[]> {
    if (patch.messageRetentionDays !== undefined && (patch.messageRetentionDays < 1 || patch.messageRetentionDays > 3650)) {
      return Result.fail(makeError('Settings.InvalidRetention', 'messageRetentionDays must be 1..3650.'));
    }
    if (patch.recordingRetentionDays !== undefined && (patch.recordingRetentionDays < 1 || patch.recordingRetentionDays > 3650)) {
      return Result.fail(makeError('Settings.InvalidRetention', 'recordingRetentionDays must be 1..3650.'));
    }
    if (patch.recordingConsentPolicy !== undefined && !isRecordingConsentPolicy(patch.recordingConsentPolicy)) {
      return Result.fail(makeError('Settings.InvalidRecordingConsentPolicy', 'Invalid recordingConsentPolicy.'));
    }
    for (const key of Object.keys(OFFICE_NUMERIC_RANGES) as OfficeNumericKey[]) {
      const value = patch[key];
      if (value === undefined || value === null) continue;
      if (!isWithinTechnical(OFFICE_NUMERIC_RANGES[key], value)) {
        return Result.fail(
          makeError(
            'Settings.OutOfTechnicalRange',
            `${key} must be null or an integer within ${describeTechnicalRange(OFFICE_NUMERIC_RANGES[key])}.`,
          ),
        );
      }
    }
    if (patch.meetingSoundsPolicy !== undefined && patch.meetingSoundsPolicy !== null && !isMeetingSoundsPolicy(patch.meetingSoundsPolicy)) {
      return Result.fail(makeError('Settings.InvalidSoundsPolicy', 'meetingSoundsPolicy must be all, hostOnly or off.'));
    }
    if (patch.transcriptLanguage !== undefined && patch.transcriptLanguage !== null && !isTranscriptLanguage(patch.transcriptLanguage)) {
      return Result.fail(makeError('Settings.InvalidLanguage', 'transcriptLanguage must be null, "auto" or a language code.'));
    }
    if (patch.meetingReactionCatalog !== undefined && patch.meetingReactionCatalog !== null && !isReactionCatalog(patch.meetingReactionCatalog)) {
      return Result.fail(makeError('Settings.InvalidReactionCatalog', 'meetingReactionCatalog must be null or 1..30 distinct short emojis.'));
    }
    if (patch.transcriptPrompt !== undefined && patch.transcriptPrompt !== null && patch.transcriptPrompt.trim().length > TRANSCRIPT_PROMPT_MAX_LENGTH) {
      return Result.fail(makeError('Settings.InvalidTranscriptPrompt', `transcriptPrompt must be at most ${TRANSCRIPT_PROMPT_MAX_LENGTH} characters.`));
    }
    const normalized: TenantCommunicationSettingsPatch =
      patch.transcriptPrompt === undefined ? patch : { ...patch, transcriptPrompt: patch.transcriptPrompt?.trim() || null };
    const changes = diffPatch(this.state, normalized);
    if (changes.length === 0) return Result.ok([]);
    this.state = { ...this.state, ...normalized, updatedAtUtc: now };
    return Result.ok(changes);
  }

  /** Rechaza el patch si alguna clave supera el techo efectivo (T-46). */
  static validateAgainstCeilings(patch: TenantCommunicationSettingsPatch, ceilings: EffectiveCeilings): Result<void> {
    for (const key of Object.keys(CEILING_OF) as CeilingedKey[]) {
      const value = patch[key];
      if (value === undefined || value === null) continue;
      const ceiling = ceilings[CEILING_OF[key]];
      if (value > ceiling) {
        return Result.fail(makeError('Settings.AboveCeiling', `${key} cannot exceed the effective limit of ${ceiling}.`));
      }
    }
    if (patch.guestsAllowed === true && !ceilings.guestsAllowed) {
      return Result.fail(makeError('Settings.AboveCeiling', 'Guests are not allowed for this office.'));
    }
    return Result.okVoid();
  }

  /** Baja lo que quedo por encima del techo (PlatformAdmin bajo un techo). Devuelve las correcciones. */
  applyCeilings(ceilings: EffectiveCeilings, now: Date = new Date()): ConfigurationChange[] {
    const patch: { -readonly [K in keyof TenantCommunicationSettingsPatch]: TenantCommunicationSettingsPatch[K] } = {};
    for (const key of Object.keys(CEILING_OF) as CeilingedKey[]) {
      const value = this.state[key];
      if (value === null) continue;
      const ceiling = ceilings[CEILING_OF[key]];
      if (value > ceiling) patch[key] = ceiling;
    }
    if (this.state.guestsAllowed && !ceilings.guestsAllowed) patch.guestsAllowed = false;
    const changes = diffPatch(this.state, patch);
    if (changes.length === 0) return [];
    this.state = { ...this.state, ...patch, updatedAtUtc: now };
    return changes;
  }

  toSnapshot(): TenantCommunicationSettingsSnapshot {
    return this.state;
  }

  get tenantId(): string {
    return this.state.tenantId;
  }
}
