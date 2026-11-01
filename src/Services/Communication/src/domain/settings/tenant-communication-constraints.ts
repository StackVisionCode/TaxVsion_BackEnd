import { Result, makeError } from '../shared/result.js';
import { type ConfigurationChange, diffPatch } from './configuration-change.js';
import { type TechnicalLimitKey, describeTechnicalRange, isWithinTechnical } from './technical-limits.js';

/**
 * Aggregate root: techos por oficina que fija PlatformAdmin (patrón Signature). null = sin techo
 * propio, rige el del plan/global. Nunca los edita la oficina.
 */
export interface TenantCommunicationConstraintsSnapshot {
  readonly tenantId: string;
  readonly version: number;
  readonly maxMeetingParticipants: number | null;
  readonly maxMeetingDurationMinutes: number | null;
  readonly maxRecordingMinutesMeeting: number | null;
  readonly maxRecordingMinutesAudioCall: number | null;
  readonly maxRecordingMinutesVideoCall: number | null;
  readonly recordingAllowed: boolean | null;
  readonly guestsAllowed: boolean | null;
  /** Excepción de retención para esta oficina (null = la global de la plataforma). */
  readonly messageRetentionDays: number | null;
  readonly recordingRetentionDays: number | null;
  readonly purgeEnabled: boolean | null;
  readonly notes: string | null;
  readonly updatedByUserId: string | null;
  readonly createdAtUtc: Date;
  readonly updatedAtUtc: Date;
}

export type TenantCommunicationConstraintsPatch = Partial<
  Omit<TenantCommunicationConstraintsSnapshot, 'tenantId' | 'version' | 'updatedByUserId' | 'createdAtUtc' | 'updatedAtUtc'>
>;

const NUMERIC_RANGES: Record<
  | 'maxMeetingParticipants'
  | 'maxMeetingDurationMinutes'
  | 'maxRecordingMinutesMeeting'
  | 'maxRecordingMinutesAudioCall'
  | 'maxRecordingMinutesVideoCall'
  | 'messageRetentionDays'
  | 'recordingRetentionDays',
  TechnicalLimitKey
> = {
  maxMeetingParticipants: 'maxParticipants',
  maxMeetingDurationMinutes: 'maxMeetingDurationMinutes',
  maxRecordingMinutesMeeting: 'maxRecordingMinutes',
  maxRecordingMinutesAudioCall: 'maxRecordingMinutes',
  maxRecordingMinutesVideoCall: 'maxRecordingMinutes',
  messageRetentionDays: 'messageRetentionDays',
  recordingRetentionDays: 'recordingRetentionDays',
};

const NOTES_MAX_LENGTH = 512;

export class TenantCommunicationConstraints {
  private constructor(private state: TenantCommunicationConstraintsSnapshot) {}

  static rehydrate(snapshot: TenantCommunicationConstraintsSnapshot): TenantCommunicationConstraints {
    return new TenantCommunicationConstraints(snapshot);
  }

  /** Sin techos propios: la oficina queda bajo el plan y la plataforma. */
  static empty(tenantId: string, now: Date = new Date()): TenantCommunicationConstraints {
    return new TenantCommunicationConstraints({
      tenantId,
      version: 1,
      maxMeetingParticipants: null,
      maxMeetingDurationMinutes: null,
      maxRecordingMinutesMeeting: null,
      maxRecordingMinutesAudioCall: null,
      maxRecordingMinutesVideoCall: null,
      recordingAllowed: null,
      guestsAllowed: null,
      messageRetentionDays: null,
      recordingRetentionDays: null,
      purgeEnabled: null,
      notes: null,
      updatedByUserId: null,
      createdAtUtc: now,
      updatedAtUtc: now,
    });
  }

  update(
    patch: TenantCommunicationConstraintsPatch,
    actor: { userId: string; now?: Date },
  ): Result<ConfigurationChange[]> {
    for (const key of Object.keys(NUMERIC_RANGES) as Array<keyof typeof NUMERIC_RANGES>) {
      const value = patch[key];
      if (value === undefined || value === null) continue;
      if (!isWithinTechnical(NUMERIC_RANGES[key], value)) {
        return Result.fail(
          makeError(
            'Settings.OutOfTechnicalRange',
            `${key} must be null or an integer within ${describeTechnicalRange(NUMERIC_RANGES[key])}.`,
          ),
        );
      }
    }
    if (patch.notes !== undefined && patch.notes !== null && patch.notes.length > NOTES_MAX_LENGTH) {
      return Result.fail(makeError('Settings.NotesTooLong', `notes must be at most ${NOTES_MAX_LENGTH} characters.`));
    }

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

  toSnapshot(): TenantCommunicationConstraintsSnapshot {
    return this.state;
  }

  get tenantId(): string {
    return this.state.tenantId;
  }
}
