/** Desplaza los segmentos de una pista por su inicio y les pone el hablante. */
export function shiftAndLabel(segments, startOffsetMs, label) {
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
export function mergeSegments(tracks) {
    return tracks.flat().sort((a, b) => a.startMs - b.startMs || a.endMs - b.endMs);
}
/** Hablantes con al menos un segmento, en orden de primera aparicion. */
export function collectSpeakers(segments) {
    const seen = new Map();
    for (const segment of segments) {
        if (!segment.speakerUserId || seen.has(segment.speakerUserId))
            continue;
        seen.set(segment.speakerUserId, {
            userId: segment.speakerUserId,
            displayName: segment.speakerName ?? segment.speakerUserId,
        });
    }
    return [...seen.values()];
}
export function formatTimestamp(ms) {
    const totalSeconds = Math.max(0, Math.floor(ms / 1000));
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    return [hours, minutes, seconds].map((n) => String(n).padStart(2, '0')).join(':');
}
/** TXT: `[hh:mm:ss] Nombre: texto`, o `[hh:mm:ss] texto` sin hablante. */
export function formatTranscriptTxt(segments) {
    const lines = segments.map((segment) => {
        const speaker = segment.speakerName ? `${segment.speakerName}: ` : '';
        return `[${formatTimestamp(segment.startMs)}] ${speaker}${segment.text}`;
    });
    return lines.length === 0 ? '' : `${lines.join('\n')}\n`;
}
export function buildTranscriptJsonV2(input) {
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
export function plainText(segments) {
    return segments
        .map((segment) => segment.text)
        .join(' ')
        .replace(/\s+/g, ' ')
        .trim();
}
export function countWords(text) {
    return text.length === 0 ? 0 : text.split(/\s+/).filter(Boolean).length;
}
/** Recorta a `maxChars` unidades UTF-16 (NVARCHAR) sin dejar un surrogate suelto. */
export function truncateInlineText(text, maxChars) {
    const trimmed = text.trim();
    if (trimmed.length <= maxChars)
        return trimmed;
    let cut = trimmed.slice(0, maxChars);
    const last = cut.charCodeAt(cut.length - 1);
    if (last >= 0xd800 && last <= 0xdbff)
        cut = cut.slice(0, -1);
    return cut.trimEnd();
}
/** Idioma mas frecuente entre pistas (el primero en caso de empate). */
export function pickDominantLanguage(languages) {
    const counts = new Map();
    for (const language of languages) {
        if (language)
            counts.set(language, (counts.get(language) ?? 0) + 1);
    }
    let best = null;
    let bestCount = 0;
    for (const [language, count] of counts) {
        if (count > bestCount) {
            best = language;
            bestCount = count;
        }
    }
    return best;
}
//# sourceMappingURL=transcript-format.js.map