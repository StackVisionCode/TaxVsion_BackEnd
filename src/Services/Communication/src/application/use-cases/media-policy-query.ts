import type { EffectiveCommunicationSettings, EffectiveMediaPolicy } from '../../domain/settings/effective-communication-settings.js';
import type { MeetingSoundsPolicy } from '../../domain/settings/technical-limits.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';

/**
 * Lo que un cliente (CRM o portal) necesita para capturar, medir y adaptar media sin ningun numero
 * propio: politica efectiva de la oficina + preferencias del usuario ya aplicadas (F4).
 */
export interface MediaPolicyDto {
  readonly version: number;
  readonly quality: EffectiveMediaPolicy['quality'];
  readonly video: EffectiveMediaPolicy['video'];
  readonly audio: EffectiveMediaPolicy['audio'];
  readonly screenShare: EffectiveMediaPolicy['screenShare'];
  readonly sounds: { readonly policy: MeetingSoundsPolicy; readonly volumePct: number; readonly repeatGapMs: number; readonly muted: boolean };
  readonly echo: EffectiveMediaPolicy['echo'];
  /** F5 (D-50): el cliente pinta el picker con `catalog` y limita localmente con `perUserPer10s`. */
  readonly reactions: { readonly enabled: boolean; readonly catalog: readonly string[]; readonly durationSeconds: number; readonly perUserPer10s: number };
  /** F5 (T-69): tope de `meeting.speaking`. */
  readonly speaking: EffectiveMediaPolicy['speaking'];
  readonly preferences: {
    readonly joinMuted: boolean;
    readonly joinCameraOff: boolean;
    readonly preferAudioOnly: boolean;
    readonly autoPictureInPicture: boolean;
    readonly audioProcessing: NonNullable<EffectiveCommunicationSettings['user']>['audioProcessing'];
  };
}

export function toMediaPolicyDto(effective: EffectiveCommunicationSettings): MediaPolicyDto {
  const user = effective.user;
  const office = effective.media.audio.processing;
  return {
    version: effective.platformVersion,
    quality: effective.media.quality,
    video: effective.media.video,
    audio: {
      ...effective.media.audio,
      // Preferencia del usuario sobre el default de la oficina (D-46).
      processing: {
        echoCancellation: user?.audioProcessing.echoCancellation ?? office.echoCancellation,
        noiseSuppression: user?.audioProcessing.noiseSuppression ?? office.noiseSuppression,
        autoGainControl: user?.audioProcessing.autoGainControl ?? office.autoGainControl,
      },
    },
    screenShare: effective.media.screenShare,
    sounds: { policy: effective.meeting.soundsPolicy, ...effective.media.sounds, muted: user?.soundsMuted ?? false },
    echo: effective.media.echo,
    reactions: {
      enabled: effective.media.reactions.enabled,
      catalog: effective.media.reactions.catalog,
      durationSeconds: effective.media.reactions.durationSeconds,
      perUserPer10s: effective.media.reactions.perUserPer10s,
    },
    speaking: effective.media.speaking,
    preferences: {
      joinMuted: user?.joinMuted ?? false,
      joinCameraOff: user?.joinCameraOff ?? false,
      preferAudioOnly: user?.preferAudioOnly ?? false,
      autoPictureInPicture: user?.autoPictureInPicture ?? false,
      audioProcessing: user?.audioProcessing ?? { echoCancellation: null, noiseSuppression: null, autoGainControl: null },
    },
  };
}

export async function getMediaPolicy(
  input: { tenantId: string; userId: string },
  deps: { effectiveSettings: Pick<EffectiveSettingsResolver, 'resolveForUser'> },
): Promise<MediaPolicyDto> {
  return toMediaPolicyDto(await deps.effectiveSettings.resolveForUser(input.tenantId, input.userId));
}
