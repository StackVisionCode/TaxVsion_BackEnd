import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config.js';
import { NoAudioStreamError, ProcessTimeoutError, isExecTimeout } from '../errors.js';

const execFileAsync = promisify(execFile);

/** Argumentos de ffmpeg: filtro configurable (vacio = sin -af) → WAV PCM 16 kHz mono. */
export function buildTranscodeArgs(inputPath: string, outputWavPath: string, audioFilter: string): string[] {
  const args = ['-y', '-hide_banner', '-loglevel', 'error', '-i', inputPath, '-vn'];
  if (audioFilter.trim()) args.push('-af', audioFilter.trim());
  args.push('-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', outputWavPath);
  return args;
}

/**
 * whisper.cpp solo lee WAV PCM 16 kHz mono. El preprocesado (highpass +
 * loudnorm + soxr) iguala niveles entre pistas y quita rumble antes de whisper.
 */
export async function transcodeToWav16kMono(inputPath: string, outputWavPath: string, timeoutMs: number): Promise<void> {
  try {
    await execFileAsync(config.ffmpeg.binPath, buildTranscodeArgs(inputPath, outputWavPath, config.ffmpeg.audioFilter), {
      timeout: timeoutMs,
      maxBuffer: 8 * 1024 * 1024,
    });
  } catch (err) {
    if (isExecTimeout(err)) throw new ProcessTimeoutError(`ffmpeg exceeded ${timeoutMs} ms for ${inputPath}`);
    throw err;
  }
}

export interface MediaProbe {
  readonly audioStreamCount: number;
  /** null si el contenedor no la declara (WebM de MediaRecorder suele venir sin duracion). */
  readonly durationSeconds: number | null;
}

interface FfprobeJson {
  readonly streams?: readonly unknown[];
  readonly format?: { readonly duration?: unknown };
}

export function parseFfprobeJson(stdout: string): MediaProbe {
  const parsed = JSON.parse(stdout) as FfprobeJson;
  const duration = Number(parsed.format?.duration);
  return {
    audioStreamCount: Array.isArray(parsed.streams) ? parsed.streams.length : 0,
    durationSeconds: Number.isFinite(duration) && duration > 0 ? duration : null,
  };
}

/**
 * Cuenta pistas de audio y lee la duracion ANTES del transcode (bug #245 y
 * limite TooLong). 0 pistas => NoAudioStreamError. Si ffprobe no puede leer
 * el archivo, su error se propaga tal cual.
 */
export async function probeMedia(inputPath: string): Promise<MediaProbe> {
  const { stdout } = await execFileAsync(
    config.ffmpeg.ffprobeBinPath,
    ['-v', 'error', '-select_streams', 'a', '-show_entries', 'stream=index:format=duration', '-of', 'json', inputPath],
    { timeout: config.ffmpeg.ffprobeTimeoutMs },
  );
  const probe = parseFfprobeJson(stdout);
  if (probe.audioStreamCount === 0) {
    throw new NoAudioStreamError(`ffprobe reported 0 audio streams for ${inputPath}`);
  }
  return probe;
}

/** Duracion exacta del WAV ya transcodificado (respaldo cuando el original no la declara). */
export async function probeDurationSeconds(inputPath: string): Promise<number | null> {
  const { stdout } = await execFileAsync(
    config.ffmpeg.ffprobeBinPath,
    ['-v', 'error', '-show_entries', 'format=duration', '-of', 'json', inputPath],
    { timeout: config.ffmpeg.ffprobeTimeoutMs },
  );
  return parseFfprobeJson(stdout).durationSeconds;
}
