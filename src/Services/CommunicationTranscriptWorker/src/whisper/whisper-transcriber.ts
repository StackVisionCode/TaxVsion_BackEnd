import { execFile } from 'node:child_process';
import { availableParallelism, setPriority } from 'node:os';
import { promisify } from 'node:util';
import { readFile, rm } from 'node:fs/promises';
import { config } from '../config.js';
import { ProcessTimeoutError, isExecTimeout } from '../errors.js';
import { buildWhisperArgs, resolveWhisperThreads } from './whisper-args.js';
import { correctedLanguage, extractDetectedLanguage, parseWhisperJson, type WhisperSegment } from './whisper-output.js';
import { languageCorrectionsTotal } from '../telemetry/metrics.js';
import { logger } from '../logger.js';

const execFileAsync = promisify(execFile);

export interface TranscriptionResult {
  readonly segments: readonly WhisperSegment[];
  readonly detectedLanguage: string | null;
  /** Segundos de reloj de la corrida de whisper (para el factor de tiempo real). */
  readonly processingSeconds: number;
}

export interface TranscribeOptions {
  readonly language: string;
  readonly prompt: string | null;
  readonly timeoutMs: number;
}

/**
 * Shell-out a whisper-cli (compilado en el Dockerfile, sin Python). Salida
 * `-oj` (segmentos con offsets en ms); el JSON se lee y se borra aqui.
 *
 * Con idioma `auto`, si whisper detecta un idioma que las oficinas no usan (en clips cortos confunde
 * español con italiano o portugués) se repite forzando el permitido más parecido. No se desempata por
 * confianza: forzado a inglés, whisper traduce el español con mucha seguridad.
 */
export async function transcribeWav(wavPath: string, outPrefix: string, opts: TranscribeOptions): Promise<TranscriptionResult> {
  const first = await runWhisper(wavPath, outPrefix, opts, opts.language);
  const detected = first.detectedLanguage;
  if (opts.language !== 'auto' || first.segments.length === 0 || !detected) return first;
  const chosen = correctedLanguage(detected, config.whisper.allowedLanguages, config.whisper.languageFallbacks);
  if (!chosen) return first;

  const second = await runWhisper(wavPath, outPrefix, opts, chosen);
  languageCorrectionsTotal.inc({ detected, chosen });
  logger.warn({ detected, chosen }, 'detected transcript language is not allowed; transcribed again');
  return { ...second, detectedLanguage: chosen, processingSeconds: first.processingSeconds + second.processingSeconds };
}

async function runWhisper(wavPath: string, outPrefix: string, opts: TranscribeOptions, language: string): Promise<TranscriptionResult> {
  const args = buildWhisperArgs({
    modelPath: config.whisper.modelPath,
    wavPath,
    outPrefix,
    language,
    prompt: opts.prompt,
    maxContext: config.whisper.maxContext,
    entropyThold: config.whisper.entropyThold,
    beamSize: config.whisper.beamSize,
    threads: resolveWhisperThreads(config.whisper.threads, availableParallelism()),
    suppressNst: config.whisper.suppressNst,
    vadModelPath: config.whisper.vadModelPath,
    vad: config.whisper.vad,
    carryInitialPrompt: config.whisper.carryInitialPrompt,
  });

  const startedAt = process.hrtime.bigint();
  let stdout: string;
  try {
    const child = execFileAsync(config.whisper.binPath, args, {
      maxBuffer: 64 * 1024 * 1024,
      timeout: opts.timeoutMs,
    });
    lowerPriority(child.child?.pid, config.whisper.nice);
    ({ stdout } = await child);
  } catch (err) {
    if (isExecTimeout(err)) throw new ProcessTimeoutError(`whisper-cli exceeded ${opts.timeoutMs} ms`);
    throw err;
  }
  const processingSeconds = Number(process.hrtime.bigint() - startedAt) / 1e9;

  const jsonPath = `${outPrefix}.json`;
  const raw = await readFile(jsonPath, 'utf-8');
  await rm(jsonPath, { force: true });

  const parsed = parseWhisperJson(raw);
  const fixedLanguage = language === 'auto' ? null : language;
  return {
    segments: parsed.segments,
    detectedLanguage: parsed.language ?? extractDetectedLanguage(stdout) ?? fixedLanguage,
    processingSeconds,
  };
}

/**
 * Baja la prioridad de whisper-cli: en la VM compartida, una transcripción (que puede esperar) no le
 * quita CPU a las llamadas y reuniones en vivo. Sin permiso o sin soporte, sigue a prioridad normal.
 */
function lowerPriority(pid: number | undefined, nice: number): void {
  if (!pid || nice <= 0) return;
  try {
    setPriority(pid, nice);
  } catch {
    // Contenedor sin permiso para cambiar prioridades: no es motivo para fallar la transcripción.
  }
}

