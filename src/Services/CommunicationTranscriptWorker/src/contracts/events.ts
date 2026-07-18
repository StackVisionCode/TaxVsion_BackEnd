/**
 * Fase Transcript 1 — contratos de integration events que este worker publica
 * al exchange fanout `taxvision-events` (mismo bus que Communication). Estos
 * tipos son solo para el lado publisher: no hay consumer en este proceso.
 *
 * `TranscriptFailedEvent` es el evento que reporta CUALQUIER fallo del
 * pipeline (download/ffmpeg/whisper/upload/publish/audio) con su reason
 * especifico — wireado en `pipeline.ts` desde Fase Transcript 2. El consumer
 * del lado de Communication para `communication.{call,meeting}.
 * transcript_failed.v1` ya existia (transcript-consumers.ts, Communication)
 * antes de que este worker publicara nada ahi.
 *
 * @since Fase Transcript 3 — `RecordingValidationFailedEvent` (el mecanismo
 * de Fase Backend 8 para "sin audio", con su propio publisher y eventType
 * `recording_validation_failed.v1`) se eliminó de este archivo junto con
 * `rabbit/validation-failed-publisher.ts` y `media/audio-probe.ts`: el
 * chequeo de audio ahora usa `probeAudioStreams()` (media/audio-transcoder.ts)
 * y reporta via este mismo `TranscriptFailedEvent` con
 * `failureReason: 'NoAudioStream'`, no via un evento separado. El consumer
 * `recording_validation_failed.v1` sigue existiendo del lado de Communication
 * (transcript-consumers.ts) pero ya no recibe trafico de este worker.
 */

export type RecordingTargetKind = 'call' | 'meeting';

export const TranscriptFailedEventTypes: Readonly<Record<RecordingTargetKind, string>> = {
  call: 'communication.call.transcript_failed.v1',
  meeting: 'communication.meeting.transcript_failed.v1',
};

export type TranscriptFailureReason =
  | 'NoAudioStream'
  | 'FfmpegError'
  | 'WhisperError'
  | 'DownloadFailed'
  | 'UploadFailed'
  | 'Timeout';

export interface TranscriptFailedEvent {
  readonly eventId: string;
  readonly eventType: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly callId?: string;
  readonly meetingId?: string;
  readonly recordingFileId: string;
  readonly failureReason: TranscriptFailureReason;
  readonly errorMessage: string;
  readonly occurredAtUtc: string;
}
