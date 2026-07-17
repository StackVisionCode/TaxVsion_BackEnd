/**
 * Estado de una RecordingSession (grabacion de Meeting o Call). 'Idle' nunca
 * se persiste — la ausencia de fila en RecordingSession (repository.find*
 * devuelve null) ES el estado Idle desde el punto de vista del consumidor;
 * se incluye igual en el union type porque los contratos socket
 * (MeetingRecordingState/CallRecordingState, Fase Backend 1) lo exponen asi.
 *
 * Transiciones validas (aplicadas por RecordingSession, ver recording-session.ts):
 *   (sin fila) -> Requesting -> Recording -> Stopping -> Processing -> Ready
 *                      \-> Failed          \-> Failed       \-> Failed
 * Processing/Ready se alcanzan recien en Fase Backend 3 (attachRecording +
 * transcript worker) — Fase Backend 2 solo modela el state machine completo,
 * no lo cablea a esos triggers todavia.
 */
export const RecordingSessionState = {
  Idle: 'Idle',
  Requesting: 'Requesting',
  Recording: 'Recording',
  Stopping: 'Stopping',
  Processing: 'Processing',
  Ready: 'Ready',
  Failed: 'Failed',
} as const;

export type RecordingSessionState = (typeof RecordingSessionState)[keyof typeof RecordingSessionState];

export function isRecordingSessionState(value: string): value is RecordingSessionState {
  return (
    value === 'Idle' ||
    value === 'Requesting' ||
    value === 'Recording' ||
    value === 'Stopping' ||
    value === 'Processing' ||
    value === 'Ready' ||
    value === 'Failed'
  );
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
