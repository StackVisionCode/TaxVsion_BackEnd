export function parseWhisperJson(raw) {
    // whisper-cli puede escribir el JSON con BOM o espacios al final.
    const parsed = JSON.parse(raw.replace(/^﻿/, '').trim());
    const rawSegments = Array.isArray(parsed.transcription) ? parsed.transcription : [];
    const segments = [];
    for (const segment of rawSegments) {
        const text = typeof segment.text === 'string' ? segment.text.trim() : '';
        const from = Number(segment.offsets?.from);
        const to = Number(segment.offsets?.to);
        if (!text || !Number.isFinite(from) || !Number.isFinite(to))
            continue;
        segments.push({ startMs: Math.max(0, from), endMs: Math.max(from, to), text });
    }
    const language = parsed.result?.language;
    return {
        segments,
        language: typeof language === 'string' && language.trim() ? language.trim().toLowerCase() : null,
    };
}
/** Respaldo para el idioma si el JSON no lo trae (stdout de whisper-cli). */
export function extractDetectedLanguage(stdout) {
    const match = /auto-detected language:\s*([a-z]{2,3})/i.exec(stdout);
    return match?.[1]?.toLowerCase() ?? null;
}
//# sourceMappingURL=whisper-output.js.map