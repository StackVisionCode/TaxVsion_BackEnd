import { Result, makeError } from '../shared/result.js';
import { type ConfigurationChange, diffPatch } from './configuration-change.js';
import { isTranscriptLanguage } from './technical-limits.js';

/** Preferencias de autoservicio por usuario (D-02 sonidos, INV-03 §13.6 "prefer audio", idioma del transcript). */
export interface UserCommunicationPreferencesSnapshot {
  readonly tenantId: string;
  readonly userId: string;
  readonly meetingSoundsMuted: boolean;
  /** null = idioma de la oficina/plataforma. */
  readonly transcriptLanguage: string | null;
  readonly preferAudioOnly: boolean;
  // F4 (D-46): null = default de la oficina.
  readonly echoCancellation: boolean | null;
  readonly noiseSuppression: boolean | null;
  readonly autoGainControl: boolean | null;
  readonly joinMuted: boolean;
  readonly joinCameraOff: boolean;
  readonly updatedAtUtc: Date;
}

export type UserCommunicationPreferencesPatch = Partial<
  Omit<UserCommunicationPreferencesSnapshot, 'tenantId' | 'userId' | 'updatedAtUtc'>
>;

export class UserCommunicationPreferences {
  private constructor(private state: UserCommunicationPreferencesSnapshot) {}

  static rehydrate(snapshot: UserCommunicationPreferencesSnapshot): UserCommunicationPreferences {
    return new UserCommunicationPreferences(snapshot);
  }

  static defaults(tenantId: string, userId: string, now: Date = new Date()): UserCommunicationPreferences {
    return new UserCommunicationPreferences({
      tenantId,
      userId,
      meetingSoundsMuted: false,
      transcriptLanguage: null,
      preferAudioOnly: false,
      echoCancellation: null,
      noiseSuppression: null,
      autoGainControl: null,
      joinMuted: false,
      joinCameraOff: false,
      updatedAtUtc: now,
    });
  }

  update(patch: UserCommunicationPreferencesPatch, now: Date = new Date()): Result<ConfigurationChange[]> {
    if (
      patch.transcriptLanguage !== undefined &&
      patch.transcriptLanguage !== null &&
      !isTranscriptLanguage(patch.transcriptLanguage)
    ) {
      return Result.fail(makeError('Settings.InvalidLanguage', 'transcriptLanguage must be null, "auto" or a language code.'));
    }
    const changes = diffPatch(this.state, patch);
    if (changes.length === 0) return Result.ok([]);
    this.state = { ...this.state, ...patch, updatedAtUtc: now };
    return Result.ok(changes);
  }

  toSnapshot(): UserCommunicationPreferencesSnapshot {
    return this.state;
  }
}
