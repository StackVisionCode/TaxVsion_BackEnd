/**
 * Fase Backend 8 — codigos de error compartidos por el flujo de validacion
 * de grabaciones (attach + consumer de validation_failed). Centralizados en
 * un modulo unico para que un fix del backend, un handler en el frontend y
 * un consumer del bus (.NET) hablen del mismo nombre — sin este archivo, la
 * misma condicion se refactorizaba a strings distintos en cada capa.
 *
 * `EmptyFile` = subida vacia detectada al attach (bug #245 tipico: el
 * MediaRecorder nunca capturo tracks por permisos de mic denegados y el
 * frontend subio 0 bytes sin darse cuenta).
 *
 * `NoAudioStream` = el file tiene bytes pero ffprobe no encuentra track de
 * audio (bug #245 alternativo: la track de video se grabo bien, pero el
 * codec de audio jamas se negocio o el MediaRecorder no incluyo la track
 * de audio remota). Solo el worker puede detectar esto porque require
 * inspeccionar el contenido del file.
 */
export const RecordingErrors = {
  MeetingEmptyFile: {
    code: 'Meeting.Recording.EmptyFile',
    message: 'Recording file is empty (0 bytes). Check microphone permissions and MediaRecorder tracks.',
  },
  CallEmptyFile: {
    code: 'Call.Recording.EmptyFile',
    message: 'Recording file is empty (0 bytes). Check microphone permissions and MediaRecorder tracks.',
  },
  /** Nombre humano-lectura que el consumer de validation_failed persiste como FailureReason en la RecordingSession. */
  NoAudioStreamReason: 'NoAudioStream',
  EmptyFileReason: 'EmptyFile',
} as const;
