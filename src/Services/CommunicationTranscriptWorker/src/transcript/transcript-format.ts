import type { WhisperSegment } from '../whisper/whisper-output.js';

/** Segmento ya en la linea de tiempo de la grabacion, con hablante opcional. */
export interface TranscriptSegment {
  readonly startMs: number;
  readonly endMs: number;
  readonly speakerUserId: string | null;
  readonly speakerName: string | null;
  readonly text: string;
}

export interface TranscriptSpeaker {
  readonly userId: string;
  readonly displayName: string;
}

export interface TrackLabel {
  readonly userId: string;
  readonly displayName: string | null;
}

/** JSON v2 (contrato 9.4): archivo en `Transcripts`, `application/json`. */
export interface TranscriptJsonV2 {
  readonly version: 2;
  readonly language: string | null;
  readonly model: string;
  readonly vad: boolean;
  readonly durationSeconds: number;
  readonly speakers: readonly TranscriptSpeaker[];
  readonly segments: readonly TranscriptSegment[];
}

/** Desplaza los segmentos de una pista por su inicio y les pone el hablante. */
export function shiftAndLabel(
  segments: readonly WhisperSegment[],
  startOffsetMs: number,
  label: TrackLabel | null,
): TranscriptSegment[] {
  const offset = Math.max(0, startOffsetMs);
  return segments.map((segment) => ({
    startMs: segment.startMs + offset,
    endMs: segment.endMs + offset,
    speakerUserId: label?.userId ?? null,
    speakerName: label ? label.displayName?.trim() || label.userId : null,
    text: segment.text,
  }));
}

/** Entrelaza por tiempo de inicio; empate por fin y luego orden de pista (sort estable). */
export function mergeSegments(tracks: readonly (readonly TranscriptSegment[])[]): TranscriptSegment[] {
  return tracks.flat().sort((a, b) => a.startMs - b.startMs || a.endMs - b.endMs);
}

/** Hablantes con al menos un segmento, en orden de primera aparicion. */
export function collectSpeakers(segments: readonly TranscriptSegment[]): TranscriptSpeaker[] {
  const seen = new Map<string, TranscriptSpeaker>();
  for (const segment of segments) {
    if (!segment.speakerUserId || seen.has(segment.speakerUserId)) continue;
    seen.set(segment.speakerUserId, {
      userId: segment.speakerUserId,
      displayName: segment.speakerName ?? segment.speakerUserId,
    });
  }
  return [...seen.values()];
}

export function formatTimestamp(ms: number): string {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return [hours, minutes, seconds].map((n) => String(n).padStart(2, '0')).join(':');
}

/** TXT: `[hh:mm:ss] Nombre: texto`, o `[hh:mm:ss] texto` sin hablante. */
export function formatTranscriptTxt(segments: readonly TranscriptSegment[]): string {
  const lines = segments.map((segment) => {
    const speaker = segment.speakerName ? `${segment.speakerName}: ` : '';
    return `[${formatTimestamp(segment.startMs)}] ${speaker}${segment.text}`;
  });
  return lines.length === 0 ? '' : `${lines.join('\n')}\n`;
}

export function buildTranscriptJsonV2(input: {
  segments: readonly TranscriptSegment[];
  language: string | null;
  model: string;
  vad: boolean;
  durationSeconds: number;
}): TranscriptJsonV2 {
  return {
    version: 2,
    language: input.language,
    model: input.model,
    vad: input.vad,
    durationSeconds: input.durationSeconds,
    speakers: collectSpeakers(input.segments),
    segments: input.segments,
  };
}

export function plainText(segments: readonly TranscriptSegment[]): string {
  return segments
    .map((segment) => segment.text)
    .join(' ')
    .replace(/\s+/g, ' ')
    .trim();
}

export function countWords(text: string): number {
  return text.length === 0 ? 0 : text.split(/\s+/).filter(Boolean).length;
}

/** Recorta a `maxChars` unidades UTF-16 (NVARCHAR) sin dejar un surrogate suelto. */
export function truncateInlineText(text: string, maxChars: number): string {
  const trimmed = text.trim();
  if (trimmed.length <= maxChars) return trimmed;
  let cut = trimmed.slice(0, maxChars);
  const last = cut.charCodeAt(cut.length - 1);
  if (last >= 0xd800 && last <= 0xdbff) cut = cut.slice(0, -1);
  return cut.trimEnd();
}

/** Idioma mas frecuente entre pistas (el primero en caso de empate). */
export function pickDominantLanguage(languages: readonly (string | null)[]): string | null {
  const counts = new Map<string, number>();
  for (const language of languages) {
    if (language) counts.set(language, (counts.get(language) ?? 0) + 1);
  }
  let best: string | null = null;
  let bestCount = 0;
  for (const [language, count] of counts) {
    if (count > bestCount) {
      best = language;
      bestCount = count;
    }
  }
  return best;
}
