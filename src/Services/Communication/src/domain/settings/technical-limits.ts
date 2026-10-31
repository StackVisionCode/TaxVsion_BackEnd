/**
 * Techos técnicos de la capa de configuración (Revision C.1): el único sitio del código con
 * números de producto. Cada valor administrable vive en BD y se valida contra este rango al
 * guardar y al leer (fail-safe al `fallback` si la fila está corrupta).
 */
export interface TechnicalRange {
  readonly min: number;
  readonly max: number;
  /** Valor seguro cuando no hay fila o el dato guardado no cabe en el rango. */
  readonly fallback: number;
}

export const TECHNICAL_LIMITS = {
  maxMeetingDurationMinutes: { min: 5, max: 480, fallback: 60 },
  defaultMeetingDurationMinutes: { min: 5, max: 480, fallback: 60 },
  maxParticipants: { min: 2, max: 100, fallback: 10 },
  // D-63: mesh solo para dos (las conversaciones 1:1); con 3 o mas Chrome falla al renegociar mesh.
  meshMaxParticipants: { min: 2, max: 10, fallback: 2 },
  disconnectGraceSeconds: { min: 5, max: 120, fallback: 30 },
  scheduledMeetingExpiryHours: { min: 1, max: 168, fallback: 24 },
  emptyMeetingEndMinutes: { min: 1, max: 120, fallback: 10 },
  dominantSpeakerMaxPerSecond: { min: 1, max: 20, fallback: 5 },
  ringingTimeoutSeconds: { min: 15, max: 180, fallback: 60 },
  recordingConsentTimeoutSeconds: { min: 5, max: 120, fallback: 30 },
  // Tope duro de grabación (D-32); los defaults por plan quedan acotados por la duración máxima (C-04).
  maxRecordingMinutes: { min: 1, max: 240, fallback: 60 },
  invitationMinTtlHours: { min: 1, max: 720, fallback: 168 },
  invitationExpiryGraceHours: { min: 0, max: 168, fallback: 24 },
  meetingChatAttachmentsPerMinute: { min: 1, max: 60, fallback: 5 },
  meetingChatAttachmentsPerMeeting: { min: 1, max: 500, fallback: 20 },
  maxReactionsPerMessage: { min: 1, max: 10, fallback: 3 },
  // F8: minutos para editar un mensaje propio por tipo de chat (0 = no se edita) y tope de ediciones.
  chatEditWindowMinutesDirect: { min: 0, max: 10080, fallback: 15 },
  chatEditWindowMinutesGroup: { min: 0, max: 10080, fallback: 15 },
  chatEditWindowMinutesSupport: { min: 0, max: 10080, fallback: 5 },
  chatEditWindowMinutesMeeting: { min: 0, max: 10080, fallback: 15 },
  chatMaxEditsPerMessage: { min: 1, max: 20, fallback: 5 },
  outboxRetentionDays: { min: 1, max: 90, fallback: 7 },
  processedEventRetentionDays: { min: 1, max: 180, fallback: 14 },
  idempotencyRetentionDays: { min: 1, max: 180, fallback: 30 },
  // F4: muestreo getStats del cliente (T-48).
  qualitySampleIntervalSeconds: { min: 2, max: 10, fallback: 3 },
  // F4: umbrales de calidad (INV-03 §13.2): Excellent < Good < Unstable; Poor = el resto.
  qualityExcellentRttMs: { min: 10, max: 2000, fallback: 150 },
  qualityGoodRttMs: { min: 10, max: 3000, fallback: 300 },
  qualityUnstableRttMs: { min: 10, max: 5000, fallback: 500 },
  qualityExcellentLossPct: { min: 0, max: 50, fallback: 2 },
  qualityGoodLossPct: { min: 0, max: 50, fallback: 5 },
  qualityUnstableLossPct: { min: 0, max: 80, fallback: 10 },
  qualityExcellentJitterMs: { min: 1, max: 500, fallback: 30 },
  qualityGoodJitterMs: { min: 1, max: 1000, fallback: 50 },
  qualityUnstableJitterMs: { min: 1, max: 2000, fallback: 100 },
  // F4: histeresis para subir de nivel; bajar = 1 muestreo.
  qualityUpgradeSamples: { min: 1, max: 10, fallback: 2 },
  // F4: Poor sostenido -> modo solo audio (D-40).
  qualityPoorSustainedSamples: { min: 1, max: 20, fallback: 3 },
  // F4: muestreos buenos seguidos -> subir una capa.
  qualityRecoverySamples: { min: 1, max: 20, fallback: 3 },
  // F4: captura de camara (SFU y mesh pequeno).
  cameraMaxWidth: { min: 160, max: 3840, fallback: 1280 },
  cameraMaxHeight: { min: 120, max: 2160, fallback: 720 },
  cameraMaxFps: { min: 5, max: 60, fallback: 30 },
  // F4: captura de camara en mesh con 3+ peers.
  meshCameraMaxWidth: { min: 160, max: 1920, fallback: 640 },
  meshCameraMaxHeight: { min: 120, max: 1080, fallback: 360 },
  meshCameraMaxFps: { min: 5, max: 60, fallback: 24 },
  // F4: pantalla compartida (T-52).
  screenMaxWidth: { min: 640, max: 3840, fallback: 1920 },
  screenMaxHeight: { min: 360, max: 2160, fallback: 1080 },
  screenMaxFps: { min: 1, max: 60, fallback: 15 },
  screenVideoMaxFps: { min: 1, max: 60, fallback: 30 },
  // F4: encodings del producer de pantalla.
  screenBitrateKbps: { min: 200, max: 10000, fallback: 1500 },
  screenHighBitrateKbps: { min: 200, max: 20000, fallback: 3000 },
  // F4: Opus maxaveragebitrate.
  audioMaxAverageBitrateKbps: { min: 6, max: 510, fallback: 40 },
  // F4: mute prolongado libera el microfono (D-46); 0 = nunca.
  muteReleasesHardwareMinutes: { min: 0, max: 120, fallback: 5 },
  // F4: sonidos (T-70).
  soundsVolumePct: { min: 0, max: 100, fallback: 60 },
  soundsRepeatGapMs: { min: 0, max: 5000, fallback: 400 },
  // F4: SFU: setMaxIncomingBitrate por transport de subida.
  sfuMaxIncomingBitrateKbps: { min: 100, max: 50000, fallback: 2500 },
  // F4: SFU: muestreo transport.getStats(); 0 = apagado.
  sfuStatsSampleSeconds: { min: 0, max: 120, fallback: 10 },
  // F4: bitrate de la capa alta de camara (las capas bajas se derivan).
  cameraMaxBitrateKbps: { min: 100, max: 10000, fallback: 1250 },
  // F4: mezcla local de grabacion (C-03) mientras no exista la server-side.
  clientRecordingAudioKbps: { min: 32, max: 128, fallback: 56 },
  // F5 (D-50): reacciones efimeras del meeting.
  reactionDurationSeconds: { min: 1, max: 15, fallback: 4 },
  reactionsPerUserPer10s: { min: 1, max: 60, fallback: 5 },
  reactionsPerMeetingPerMinute: { min: 10, max: 2000, fallback: 300 },
  // F6 (T-64): recordatorio de reunion agendada; 0 = apagado.
  meetingReminderMinutesBefore: { min: 0, max: 1440, fallback: 15 },
  // F7 (T-35): aviso antes del corte por límite (0 = sin aviso) y tolerancia del corte duro del recorder/worker.
  recordingLimitWarningMinutes: { min: 0, max: 30, fallback: 5 },
  recordingLimitToleranceSeconds: { min: 0, max: 900, fallback: 120 },
  // F7 (T-33): composición del compuesto (D-29: 720p@25 por defecto).
  recordingCompositionWidth: { min: 320, max: 1920, fallback: 1280 },
  recordingCompositionHeight: { min: 180, max: 1080, fallback: 720 },
  recordingCompositionFps: { min: 10, max: 30, fallback: 25 },
  recordingCompositionVideoKbps: { min: 300, max: 6000, fallback: 1500 },
  recordingCompositionAudioKbps: { min: 64, max: 256, fallback: 128 },
  // F7: capa simulcast que se graba de cada cámara (0 = baja, 2 = alta).
  recordingCaptureSpatialLayer: { min: 0, max: 2, fallback: 2 },
  // F7 (T-37): pistas crudas (solo sirven para recomponer).
  recordingTracksRetentionDays: { min: 1, max: 90, fallback: 7 },
  // F7 (D-27): notas de voz transcritas hasta esta duración.
  voiceNoteTranscriptMaxSeconds: { min: 10, max: 1800, fallback: 600 },
  // F7 (INV-12 H-1): timeout del worker = duración máxima × factor (en %).
  transcriptWorkerTimeoutFactorPct: { min: 100, max: 1000, fallback: 150 },
  // F7: compuesto subido sin rastro en CloudStorage tras este plazo (cuota o política lo descartan) → Failed.
  recordingAvailabilityTimeoutMinutes: { min: 5, max: 240, fallback: 60 },
} as const satisfies Record<string, TechnicalRange>;

export type TechnicalLimitKey = keyof typeof TECHNICAL_LIMITS;

/** F5 (D-50): catalogo curado por defecto; la plataforma y la oficina lo cambian en BD. */
export const DEFAULT_MEETING_REACTION_CATALOG: readonly string[] = ['👍', '👏', '❤️', '😂', '😮', '🎉', '🙏', '🤔', '✅', '🔥'];
export const MEETING_REACTION_CATALOG_MAX_ITEMS = 30;
export const MEETING_REACTION_MAX_LENGTH = 16;

/** Lista no vacia de emojis distintos, cortos y sin espacios. */
export function isReactionCatalog(value: unknown): value is readonly string[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > MEETING_REACTION_CATALOG_MAX_ITEMS) return false;
  const seen = new Set<string>();
  for (const item of value) {
    if (typeof item !== 'string' || item.length === 0 || item.length > MEETING_REACTION_MAX_LENGTH) return false;
    if (/\s/.test(item) || seen.has(item)) return false;
    seen.add(item);
  }
  return true;
}

/** F7 (D-29): contenedor del compuesto; MP4 H.264/AAC por defecto, WebM VP8/Opus configurable. */
export const RECORDING_COMPOSITION_FORMATS = ['mp4', 'webm'] as const;
export type RecordingCompositionFormat = (typeof RECORDING_COMPOSITION_FORMATS)[number];

export function isRecordingCompositionFormat(value: unknown): value is RecordingCompositionFormat {
  return typeof value === 'string' && (RECORDING_COMPOSITION_FORMATS as readonly string[]).includes(value);
}

export const MEETING_SOUNDS_POLICIES = ['all', 'hostOnly', 'off'] as const;
export type MeetingSoundsPolicy = (typeof MEETING_SOUNDS_POLICIES)[number];

export function isMeetingSoundsPolicy(value: unknown): value is MeetingSoundsPolicy {
  return typeof value === 'string' && (MEETING_SOUNDS_POLICIES as readonly string[]).includes(value);
}

/** "auto" o un código de idioma corto (BCP-47 sin extensiones largas). */
const LANGUAGE_PATTERN = /^(auto|[a-z]{2,3}(-[A-Za-z0-9]{2,8})?)$/;

export function isTranscriptLanguage(value: unknown): value is string {
  return typeof value === 'string' && LANGUAGE_PATTERN.test(value);
}

export function isWithinTechnical(key: TechnicalLimitKey, value: number): boolean {
  const range = TECHNICAL_LIMITS[key];
  return Number.isInteger(value) && value >= range.min && value <= range.max;
}

/** Lectura fail-safe: lo que no cabe en el rango vuelve al fallback, nunca se propaga. */
export function clampToTechnical(key: TechnicalLimitKey, value: number | null | undefined): number {
  const range = TECHNICAL_LIMITS[key];
  if (value === null || value === undefined || !Number.isFinite(value)) return range.fallback;
  if (value < range.min) return range.min;
  if (value > range.max) return range.max;
  return Math.trunc(value);
}

export function describeTechnicalRange(key: TechnicalLimitKey): string {
  const range = TECHNICAL_LIMITS[key];
  return `${range.min}..${range.max}`;
}
