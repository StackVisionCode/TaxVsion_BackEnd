import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

export class FfmpegError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FfmpegError';
  }
}

export interface FfmpegRunner {
  run(args: readonly string[], opts: { timeoutMs: number }): Promise<void>;
}

/** ffmpeg de composición: hilos acotados por argumentos y, en Linux, `nice` para no competir con la captura. */
export function createFfmpegRunner(opts: {
  ffmpegPath: string;
  nice: number;
  platform?: NodeJS.Platform;
}): FfmpegRunner {
  const platform = opts.platform ?? process.platform;
  return {
    run(args, runOpts) {
      const useNice = platform !== 'win32' && opts.nice > 0;
      const command = useNice ? 'nice' : opts.ffmpegPath;
      const fullArgs = useNice ? ['-n', String(opts.nice), opts.ffmpegPath, ...args] : [...args];
      return new Promise<void>((resolve, reject) => {
        const child = spawn(command, fullArgs, { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true });
        let stderr = '';
        child.stderr?.on('data', (chunk: Buffer) => {
          stderr = (stderr + chunk.toString('utf-8')).slice(-4000);
        });
        const timer = setTimeout(() => {
          child.kill('SIGKILL');
          reject(new FfmpegError(`ffmpeg timed out after ${runOpts.timeoutMs} ms`));
        }, runOpts.timeoutMs);
        child.once('error', (err) => {
          clearTimeout(timer);
          reject(new FfmpegError(`ffmpeg could not start: ${err.message}`));
        });
        child.once('close', (code) => {
          clearTimeout(timer);
          if (code === 0) resolve();
          else reject(new FfmpegError(`ffmpeg exited with ${code}: ${stderr.trim().slice(-1500)}`));
        });
      });
    },
  };
}

export interface MediaProbe {
  readonly durationMs: number | null;
  readonly streams: { readonly codecType: string }[];
}

export type Prober = (file: string) => Promise<MediaProbe>;

/** ffprobe JSON: duración del contenedor (o del stream más largo si el contenedor no la trae). */
export function createProber(ffprobePath: string): Prober {
  return async (file) => {
    const { stdout } = await execFileAsync(
      ffprobePath,
      ['-v', 'error', '-show_entries', 'format=duration:stream=codec_type,duration', '-of', 'json', file],
      { timeout: 60_000, windowsHide: true },
    );
    return parseProbe(stdout);
  };
}

export function parseProbe(stdout: string): MediaProbe {
  const json = JSON.parse(stdout) as {
    format?: { duration?: string };
    streams?: { codec_type?: string; duration?: string }[];
  };
  const streams = (json.streams ?? []).map((s) => ({ codecType: s.codec_type ?? 'unknown', duration: Number(s.duration) }));
  const formatDuration = Number(json.format?.duration);
  const streamMax = Math.max(0, ...streams.map((s) => (Number.isFinite(s.duration) ? s.duration : 0)));
  const seconds = Number.isFinite(formatDuration) && formatDuration > 0 ? formatDuration : streamMax > 0 ? streamMax : null;
  return {
    durationMs: seconds === null ? null : Math.round(seconds * 1000),
    streams: streams.map((s) => ({ codecType: s.codecType })),
  };
}
