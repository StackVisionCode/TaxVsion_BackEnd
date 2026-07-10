import { Result, makeError } from '../shared/result.js';

/**
 * Consolidacion del media state de un participante. Reemplaza los 4 eventos
 * separados del legacy (participant_muted, participant_video_on, ...on|off,
 * participant_screen_share_start/stop) por UNA sola forma canonica — el FE
 * consumia dos formatos en paralelo y hacia dedup manual.
 */
export interface MediaStatus {
  readonly audioEnabled: boolean;
  readonly videoEnabled: boolean;
  readonly screenSharing: boolean;
}

/**
 * Solo el propio participante puede modificar su media status. El aggregate
 * lo valida; aqui solo damos el tipo.
 */
export function makeMediaStatus(input: {
  audioEnabled: boolean;
  videoEnabled: boolean;
  screenSharing: boolean;
}): Result<MediaStatus> {
  return Result.ok({
    audioEnabled: input.audioEnabled,
    videoEnabled: input.videoEnabled,
    screenSharing: input.screenSharing,
  });
}

/**
 * Calidad de conexion — se recibe del cliente via signaling. El backend NO la
 * calcula ni interpreta; la almacena para debugging/analytics. Nunca `Unknown`
 * viene del cliente (el enum del FE lo excluye), pero es el default DB.
 */
export const ConnectionQuality = {
  Unknown: 'Unknown',
  Excellent: 'Excellent',
  Good: 'Good',
  Fair: 'Fair',
  Poor: 'Poor',
  Disconnected: 'Disconnected',
} as const;

export type ConnectionQuality = (typeof ConnectionQuality)[keyof typeof ConnectionQuality];

export function isConnectionQuality(value: string): value is ConnectionQuality {
  return (
    value === 'Unknown' ||
    value === 'Excellent' ||
    value === 'Good' ||
    value === 'Fair' ||
    value === 'Poor' ||
    value === 'Disconnected'
  );
}

export function makeConnectionQuality(input: string): Result<ConnectionQuality> {
  if (!isConnectionQuality(input)) {
    return Result.fail(makeError('Call.InvalidQuality', `Unknown ConnectionQuality '${input}'.`));
  }
  return Result.ok(input);
}
