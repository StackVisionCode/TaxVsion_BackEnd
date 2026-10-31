/**
 * Estado de una RecordingSession (grabacion de Meeting o Call). 'Idle' nunca
 * se persiste — la ausencia de fila en RecordingSession (repository.find*
 * devuelve null) ES el estado Idle desde el punto de vista del consumidor;
 * se incluye igual en el union type porque los contratos socket lo exponen asi.
 *
 * Transiciones validas (aplicadas por RecordingSession, ver recording-session.ts):
 *   (sin fila) -> Requesting -> Recording -> Stopping -> Composing -> Processing -> Ready
 *   Client (fallback C-03):                  Stopping -> Processing (attach) ----> Ready
 *   Cualquier estado no terminal -> Failed.
 * F7 (T-34): Ready = el archivo de la grabacion esta Available en CloudStorage; el transcript
 * tiene su propio estado (TranscriptArtifact) y ya no decide el de la grabacion.
 */
export const RecordingSessionState = {
  Idle: 'Idle',
  Requesting: 'Requesting',
  Recording: 'Recording',
  Stopping: 'Stopping',
  Composing: 'Composing',
  Processing: 'Processing',
  Ready: 'Ready',
  Failed: 'Failed',
} as const;

export type RecordingSessionState = (typeof RecordingSessionState)[keyof typeof RecordingSessionState];

export function isRecordingSessionState(value: string): value is RecordingSessionState {
  return (Object.values(RecordingSessionState) as string[]).includes(value);
}

export function isTerminalRecordingState(state: RecordingSessionState): boolean {
  return state === 'Ready' || state === 'Failed';
}

/** 'Meeting' | 'Call' — coincide 1:1 con RecordingSession.Scope en Prisma. */
export const RecordingScope = {
  Meeting: 'Meeting',
  Call: 'Call',
} as const;
export type RecordingScope = (typeof RecordingScope)[keyof typeof RecordingScope];

export function isRecordingScope(value: string): value is RecordingScope {
  return value === 'Meeting' || value === 'Call';
}

/** F7: quien captura. `Server` = recorder por pista (D-28); `Client` = mezcla en el navegador (fallback C-03). */
export const RecordingCaptureMode = {
  Server: 'Server',
  Client: 'Client',
} as const;
export type RecordingCaptureMode = (typeof RecordingCaptureMode)[keyof typeof RecordingCaptureMode];

export function isRecordingCaptureMode(value: string): value is RecordingCaptureMode {
  return value === 'Server' || value === 'Client';
}

/** F7: por que paso a Stopping. */
export const RecordingStopReason = {
  User: 'User',
  LimitReached: 'LimitReached',
  MeetingEnded: 'MeetingEnded',
  CallEnded: 'CallEnded',
} as const;
export type RecordingStopReason = (typeof RecordingStopReason)[keyof typeof RecordingStopReason];

export function isRecordingStopReason(value: string): value is RecordingStopReason {
  return (Object.values(RecordingStopReason) as string[]).includes(value);
}
