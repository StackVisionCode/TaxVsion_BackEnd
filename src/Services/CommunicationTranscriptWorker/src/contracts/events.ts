/**
 * Fase Transcript 1 — contrato de fallo del pipeline de transcripcion.
 * `TranscriptFailureReason` reporta CUALQUIER fallo del pipeline
 * (download/ffmpeg/whisper/upload/publish/audio) con su reason especifico —
 * wireado en `pipeline.ts` desde Fase Transcript 2.
 *
 * El mapeo kind->eventType (a que tipo de evento RabbitMQ corresponde cada
 * `failureReason`/`transcript_ready`) ya NO vive aca — se generalizo a
 * `contracts/recording-kinds.ts` (`RecordingKindMapping`) para que este
 * worker pueda ser reusado por cualquier microservicio, no solo Communication.
 * Ver ese archivo para el detalle.
 *
 * @since Fase Transcript 3 — `RecordingValidationFailedEvent` (el mecanismo
 * de Fase Backend 8 para "sin audio", con su propio publisher y eventType
 * `recording_validation_failed.v1`) se eliminó de este archivo junto con
 * `rabbit/validation-failed-publisher.ts` y `media/audio-probe.ts`: el
 * chequeo de audio ahora usa `probeAudioStreams()` (media/audio-transcoder.ts)
 * y reporta via este mismo `TranscriptFailureReason` con valor
 * `'NoAudioStream'`, no via un evento separado. El consumer
 * `recording_validation_failed.v1` sigue existiendo del lado de Communication
 * (transcript-consumers.ts) pero ya no recibe trafico de este worker.
 */
export type TranscriptFailureReason =
  | 'NoAudioStream'
  | 'FfmpegError'
  | 'WhisperError'
  | 'DownloadFailed'
  | 'UploadFailed'
  | 'Timeout';
