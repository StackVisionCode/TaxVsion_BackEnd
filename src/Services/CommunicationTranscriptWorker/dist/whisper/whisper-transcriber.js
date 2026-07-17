import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile, rm } from 'node:fs/promises';
import { config } from '../config.js';
const execFileAsync = promisify(execFile);
/**
 * Shell-out a whisper.cpp (`whisper-cli`), compilado desde fuente en el
 * Dockerfile — deliberadamente NO un binario npm-wrapped, para no arrastrar
 * Python al stack (decision de Fase 6, ver elección whisper.cpp vs
 * faster-whisper). Requiere WAV 16kHz mono como input; la conversion corre
 * antes via `transcodeToWav16kMono`.
 *
 * Fase Transcript 5 — antes se pasaba `-nt` (no-timestamps) para que el .txt
 * saliera como texto plano linea-por-segmento. Se quito: sin timestamps en
 * stdout no hay forma de derivar `durationSeconds` (el objetivo de esta fase)
 * sin depender de un segundo proceso (ej. ffprobe sobre el wav, que no es lo
 * que se pidio — "parsear whisper stdout"). Con `-nt` fuera, tanto stdout
 * como el .txt (-otxt) llevan el prefijo `[hh:mm:ss.mmm --> hh:mm:ss.mmm]`
 * por linea; se parsea la duracion de stdout y se le quita ese prefijo a
 * cada linea del .txt antes de tratarlo como el texto "limpio" que se sube a
 * CloudStorage (mismo contenido de antes, ahora derivado en vez de nativo).
 */
export async function transcribeWav(wavPath, outPrefix) {
    const args = ['-m', config.whisper.modelPath, '-f', wavPath, '-otxt', '-of', outPrefix];
    args.push('-l', config.whisper.language ?? 'auto');
    const { stdout } = await execFileAsync(config.whisper.binPath, args, {
        maxBuffer: 32 * 1024 * 1024,
        timeout: 15 * 60 * 1000,
    });
    const txtPath = `${outPrefix}.txt`;
    const rawText = await readFile(txtPath, 'utf-8');
    await rm(txtPath, { force: true });
    const text = stripSegmentTimestamps(rawText).trim();
    const wordCount = text.length === 0 ? 0 : text.split(/\s+/).filter(Boolean).length;
    return {
        text,
        detectedLanguage: extractDetectedLanguage(stdout) ?? config.whisper.language,
        durationSeconds: parseDurationSeconds(stdout),
        wordCount,
    };
}
export function extractDetectedLanguage(stdout) {
    const match = /auto-detected language:\s*([a-z]{2})/i.exec(stdout);
    return match?.[1]?.toLowerCase() ?? null;
}
const SEGMENT_TIMESTAMP_PATTERN = /\[(\d{2}):(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})\.(\d{3})\]/g;
const LEADING_SEGMENT_TIMESTAMP_PATTERN = /^\s*\[\d{2}:\d{2}:\d{2}\.\d{3}\s*-->\s*\d{2}:\d{2}:\d{2}\.\d{3}\]\s*/;
/** Quita el prefijo `[hh:mm:ss.mmm --> hh:mm:ss.mmm]` de cada linea, si esta presente. */
export function stripSegmentTimestamps(rawText) {
    return rawText
        .split(/\r?\n/)
        .map((line) => line.replace(LEADING_SEGMENT_TIMESTAMP_PATTERN, ''))
        .join('\n');
}
/**
 * Duracion total = timestamp de fin del ULTIMO segmento impreso por
 * whisper.cpp. Si no hay ningun segmento con timestamp (audio vacio, formato
 * de salida distinto al esperado), devuelve 0 en vez de fallar — la
 * duracion es metadata de previsualizacion, no algo que deba tumbar el
 * pipeline si no se puede derivar.
 */
export function parseDurationSeconds(stdout) {
    let lastMatch = null;
    for (const match of stdout.matchAll(SEGMENT_TIMESTAMP_PATTERN)) {
        lastMatch = match;
    }
    if (!lastMatch)
        return 0;
    const [, , , , , endH, endM, endS, endMs] = lastMatch;
    const totalSeconds = Number(endH) * 3600 + Number(endM) * 60 + Number(endS) + Number(endMs) / 1000;
    return Math.round(totalSeconds);
}
//# sourceMappingURL=whisper-transcriber.js.map