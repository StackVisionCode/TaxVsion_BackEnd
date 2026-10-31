/**
 * Parser puro de la salida `-oj` de whisper-cli: `transcription[]` con
 * `offsets.from/to` en ms y `text`, y `result.language`. Con VAD, whisper.cpp
 * ya remapea los offsets a la linea de tiempo del audio original.
 */
export interface WhisperSegment {
  readonly startMs: number;
  readonly endMs: number;
  readonly text: string;
}

export interface WhisperJsonResult {
  readonly segments: readonly WhisperSegment[];
  readonly language: string | null;
}

interface RawWhisperJson {
  readonly result?: { readonly language?: unknown };
  readonly transcription?: unknown;
}

interface RawSegment {
  readonly offsets?: { readonly from?: unknown; readonly to?: unknown };
  readonly text?: unknown;
}

export function parseWhisperJson(raw: string): WhisperJsonResult {
  // whisper-cli puede escribir el JSON con BOM o espacios al final.
  const parsed = JSON.parse(raw.replace(/^﻿/, '').trim()) as RawWhisperJson;
  const rawSegments = Array.isArray(parsed.transcription) ? (parsed.transcription as RawSegment[]) : [];

  const segments: WhisperSegment[] = [];
  for (const segment of rawSegments) {
    const text = typeof segment.text === 'string' ? segment.text.trim() : '';
    const from = Number(segment.offsets?.from);
    const to = Number(segment.offsets?.to);
    if (!text || !Number.isFinite(from) || !Number.isFinite(to)) continue;
    segments.push({ startMs: Math.max(0, from), endMs: Math.max(from, to), text });
  }

  const language = parsed.result?.language;
  return {
    segments,
    language: typeof language === 'string' && language.trim() ? language.trim().toLowerCase() : null,
  };
}

/** Respaldo para el idioma si el JSON no lo trae (stdout de whisper-cli). */
export function extractDetectedLanguage(stdout: string): string | null {
  const match = /auto-detected language:\s*([a-z]{2,3})/i.exec(stdout);
  return match?.[1]?.toLowerCase() ?? null;
}
