import { execFile } from 'node:child_process';
import path from 'node:path';
import { promisify } from 'node:util';
import type { FfmpegRunner } from './ffmpeg-runner.js';
import type { ComposeTrack } from './timeline-model.js';

const execFileAsync = promisify(execFile);

export interface FrameSize {
  readonly width: number;
  readonly height: number;
}

/** Tamaños distintos de los keyframes de una pista de video (VP8 solo cambia de tamaño en un keyframe). */
export type KeyframeSizeProber = (file: string) => Promise<FrameSize[]>;

export function createKeyframeSizeProber(ffprobePath: string): KeyframeSizeProber {
  return async (file) => {
    const { stdout } = await execFileAsync(
      ffprobePath,
      ['-v', 'error', '-skip_frame', 'nokey', '-select_streams', 'v:0', '-show_entries', 'frame=width,height', '-of', 'csv=p=0', file],
      { timeout: 120_000, windowsHide: true, maxBuffer: 16 * 1024 * 1024 },
    );
    return distinctSizes(stdout);
  };
}

export function distinctSizes(csv: string): FrameSize[] {
  const seen = new Map<string, FrameSize>();
  for (const line of csv.split(/\r?\n/)) {
    const [w, h] = line.split(',').map((n) => Number.parseInt(n, 10));
    if (w && h && w > 0 && h > 0) seen.set(`${w}x${h}`, { width: w, height: h });
  }
  return [...seen.values()];
}

/** Por debajo de esto es un marcador (lienzo negro de 2x2 de una cámara apagada), no imagen real. */
const MIN_REAL_SIDE = 16;

/** Destino: el tamaño real más grande (por área), par. null si no hay ninguno real. */
export function targetSize(sizes: readonly FrameSize[]): FrameSize | null {
  const real = sizes.filter((s) => s.width >= MIN_REAL_SIDE && s.height >= MIN_REAL_SIDE);
  if (real.length === 0) return null;
  const best = real.reduce((a, b) => (b.width * b.height > a.width * a.height ? b : a));
  return { width: best.width - (best.width % 2), height: best.height - (best.height % 2) };
}

/** Reescala cada frame al destino (con bandas) conservando los tiempos; intermedio rápido y casi sin pérdida. */
export function normalizeArgs(input: string, output: string, size: FrameSize): string[] {
  const { width: w, height: h } = size;
  return [
    '-hide_banner', '-loglevel', 'error', '-y', '-i', input, '-an',
    '-vf', `scale=${w}:${h}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=${w}:${h}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1`,
    '-fps_mode', 'passthrough', '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '18', '-pix_fmt', 'yuv420p', '-f', 'matroska', output,
  ];
}

/**
 * Una pista que cambia de tamaño a mitad (capa simulcast que baja con la carga del emisor, pantalla que
 * arranca con el marcador de 2x2) haría caer al grafo de composición, que no se reconfigura en caliente.
 * Solo esas pistas pasan por un intermedio de tamaño fijo; las demás se usan tal cual.
 */
export async function normalizeVideoTracks(
  tracks: readonly ComposeTrack[],
  workDir: string,
  deps: { runner: FfmpegRunner; sizes: KeyframeSizeProber; timeoutMs: () => number },
): Promise<ComposeTrack[]> {
  const out: ComposeTrack[] = [];
  for (const track of tracks) {
    if (track.kind !== 'video') {
      out.push(track);
      continue;
    }
    const sizes = await deps.sizes(track.file).catch(() => []);
    const target = sizes.length > 1 ? targetSize(sizes) : null;
    if (!target) {
      out.push(track);
      continue;
    }
    const normalized = path.join(workDir, `norm_${track.trackId}.mkv`);
    await deps.runner.run(normalizeArgs(track.file, normalized, target), { timeoutMs: deps.timeoutMs() });
    out.push({ ...track, file: normalized });
  }
  return out;
}
