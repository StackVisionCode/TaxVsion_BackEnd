/** Error de dominio del recorder: se serializa como `{code, message}` con su status HTTP. */
export class RecorderError extends Error {
  constructor(
    public readonly statusCode: number,
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'RecorderError';
  }
}

export const Errors = {
  invalidRequest: (message: string) => new RecorderError(400, 'Recording.InvalidRequest', message),
  invalidToken: () => new RecorderError(401, 'Auth.InvalidToken', 'Service token could not be verified.'),
  clientNotAllowed: () => new RecorderError(403, 'Auth.ClientNotAllowed', 'This service client is not allowed.'),
  sessionNotFound: () => new RecorderError(404, 'Recording.SessionNotFound', 'Recording session not found.'),
  sessionClosed: () => new RecorderError(409, 'Recording.SessionClosed', 'Recording session no longer accepts tracks.'),
  videoNotCaptured: () =>
    new RecorderError(409, 'Recording.VideoNotCaptured', 'Video tracks are not captured for this session.'),
  trackOpening: () => new RecorderError(409, 'Recording.TrackOpening', 'This track is already being opened.'),
  trackClosed: () => new RecorderError(409, 'Recording.TrackClosed', 'This track was already closed.'),
  sessionActive: () => new RecorderError(409, 'Recording.SessionActive', 'Recording session is still capturing.'),
  compositionInProgress: () =>
    new RecorderError(409, 'Recording.CompositionInProgress', 'A composition is already queued or running.'),
  tracksPurged: () => new RecorderError(409, 'Recording.TracksPurged', 'Raw tracks are no longer available.'),
  capacityExceeded: () =>
    new RecorderError(503, 'Recording.CapacityExceeded', 'Recorder reached its maximum of active sessions.'),
  portsExhausted: () => new RecorderError(503, 'Recording.PortsExhausted', 'No free RTP ports in the configured range.'),
  captureFailed: (message: string) => new RecorderError(503, 'Recording.CaptureFailed', message),
} as const;
