import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { config } from '../config.js';

const execFileAsync = promisify(execFile);

/**
 * whisper.cpp solo lee WAV PCM 16kHz mono — las grabaciones llegan en el
 * formato que produjo `MediaRecorder` en el navegador (webm/opus tipico).
 * ffmpeg hace la conversion; se instala via apt en el Dockerfile, no es
 * dependencia npm.
 */
export async function transcodeToWav16kMono(inputPath: string, outputWavPath: string): Promise<void> {
  await execFileAsync(
    config.ffmpeg.binPath,
    ['-y', '-i', inputPath, '-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', outputWavPath],
    { timeout: 5 * 60 * 1000 },
  );
}

export class NoAudioStreamError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'NoAudioStreamError';
  }
}

/**
 * Fase Transcript 3 (bug #245) — cuenta streams de audio ANTES de gastar
 * CPU/tiempo en el transcode+whisper: si el MediaRecorder del frontend nunca
 * capturo audio (permisos de mic denegados, camera-only), ffmpeg iba a
 * fallar mas adelante con un exit code opaco. Reemplaza al chequeo
 * equivalente que vivia en `media/audio-probe.ts` (Fase Backend 8,
 * `hasAudioStream`, ya eliminado) — mismo comando ffprobe, ahora devuelve el
 * conteo real de streams (`stream=index`, una linea por track de audio) en
 * vez de un boolean.
 *
 * Si ffprobe mismo crashea (file corrupto, formato no reconocido) el error
 * de `execFile` se propaga tal cual — el caller (pipeline.ts) no distingue
 * "0 streams detectados" de "ffprobe no pudo ni leer el file": ambos
 * significan "no pudimos confirmar audio utilizable" y se reportan igual.
 */
export async function probeAudioStreams(inputPath: string): Promise<number> {
  const { stdout } = await execFileAsync(
    config.ffmpeg.ffprobeBinPath,
    ['-v', 'error', '-select_streams', 'a', '-show_entries', 'stream=index', '-of', 'csv=p=0', inputPath],
    { timeout: 30_000 },
  );
  const count = stdout
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean).length;
  if (count === 0) {
    throw new NoAudioStreamError(`ffprobe reported 0 audio streams for ${inputPath}`);
  }
  return count;
}
