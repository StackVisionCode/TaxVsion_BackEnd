import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { CompositionSettings, Timeline } from '../contracts/api.js';
import type { ManifestTrack, SessionManifest } from '../spool/manifest.js';
import { concatLine } from './escape.js';
import { buildFinalCommand, buildSegmentCommand, type EncodeOptions } from './filter-graph.js';
import type { FfmpegRunner, MediaProbe, Prober } from './ffmpeg-runner.js';
import { buildSegments } from './layout.js';
import { buildTimelineModel, type ComposeTrack } from './timeline-model.js';
import { prepareAvatars, type AvatarSource } from './avatars.js';
import { normalizeVideoTracks, type KeyframeSizeProber } from './normalize.js';

export interface UsableTrack extends ComposeTrack {
  readonly manifest: ManifestTrack;
  readonly probedDurationMs: number | null;
}

export interface ResolvedInput {
  readonly windowMs: number;
  readonly tracks: UsableTrack[];
}

/**
 * Pistas utilizables (archivo con datos y stream del tipo correcto) y ventana de la composición:
 * stop → si no, el final de la última pista; siempre acotada por el corte duro (max + tolerancia).
 */
export async function resolveInput(
  manifest: SessionManifest,
  trackPath: (file: string) => string,
  fileSize: (file: string) => Promise<number>,
  probe: Prober,
): Promise<ResolvedInput> {
  const startedAt = Date.parse(manifest.startedAtUtc);
  const candidates: { track: ManifestTrack; file: string; probe: MediaProbe }[] = [];
  for (const track of manifest.tracks) {
    if (track.kind === 'video' && !manifest.captureVideo) continue;
    const file = trackPath(track.file);
    if ((await fileSize(file)) <= 0) continue;
    const result = await probe(file).catch(() => null);
    if (!result || !result.streams.some((s) => s.codecType === track.kind)) continue;
    candidates.push({ track, file, probe: result });
  }

  const limitMs = (manifest.maxDurationSeconds + manifest.toleranceSeconds) * 1000;
  const hardStopMs = manifest.hardStoppedAtUtc ? Date.parse(manifest.hardStoppedAtUtc) - startedAt : Infinity;
  const stopMs = manifest.stop?.stoppedAtUtc ? Date.parse(manifest.stop.stoppedAtUtc) - startedAt : null;
  const fallbackEnd = Math.max(
    0,
    ...candidates.map((c) => c.track.endedOffsetMs ?? c.track.startOffsetMs + (c.probe.durationMs ?? 0)),
  );
  const windowMs = Math.max(0, Math.round(Math.min(stopMs ?? fallbackEnd, hardStopMs, limitMs)));

  const tracks: UsableTrack[] = [];
  for (const c of candidates) {
    const startMs = Math.max(0, c.track.startOffsetMs);
    const ends = [windowMs, c.track.endedOffsetMs ?? Infinity];
    if (c.probe.durationMs !== null) ends.push(c.track.startOffsetMs + c.probe.durationMs);
    const endMs = Math.min(...ends);
    if (endMs <= startMs) continue;
    tracks.push({
      trackId: c.track.trackId,
      userId: c.track.userId,
      kind: c.track.kind,
      source: c.track.source,
      file: c.file,
      startMs,
      endMs,
      manifest: c.track,
      probedDurationMs: c.probe.durationMs,
    });
  }
  return { windowMs, tracks };
}

export interface ComposeRequest {
  readonly startedAtUtc: string;
  readonly captureVideo: boolean;
  readonly composition: CompositionSettings;
  readonly timeline: Timeline;
  readonly windowMs: number;
  readonly tracks: readonly ComposeTrack[];
  readonly workDir: string;
  readonly outputPath: string;
}

export interface ComposeOutcome {
  readonly segments: number;
  readonly videoTrackCount: number;
}

/** Recortar una foto de 512 px tarda milisegundos; el tope solo evita colgarse con un archivo raro. */
const AVATAR_RENDER_TIMEOUT_MS = 30_000;

/** Segmentos de video (si se captura video) → concat → mux con la mezcla de audio. */
export async function composeRecording(
  req: ComposeRequest,
  deps: {
    runner: FfmpegRunner;
    fontFile: string;
    threads: number;
    timeoutMs: number;
    /** null = sin fotos (iniciales). */
    avatars?: AvatarSource | null;
    /** Tamaños de keyframes por pista: las que cambian de tamaño se normalizan antes de componer. */
    sizes?: KeyframeSizeProber;
    onAvatarError?: (fileId: string, err: unknown) => void;
  },
): Promise<ComposeOutcome> {
  const deadline = Date.now() + deps.timeoutMs;
  const remaining = () => {
    const left = deadline - Date.now();
    if (left <= 0) throw new Error('composition timed out');
    return left;
  };
  // Absoluta: el demuxer concat resuelve las rutas relativas contra la carpeta de la lista, no contra el cwd.
  const workDir = path.resolve(req.workDir);
  await rm(workDir, { recursive: true, force: true });
  await mkdir(workDir, { recursive: true });

  const { composition } = req;
  const avatars =
    req.captureVideo && deps.avatars
      ? await prepareAvatars(req.timeline.participants, workDir, {
          source: deps.avatars,
          runner: deps.runner,
          timeoutMs: Math.min(remaining(), AVATAR_RENDER_TIMEOUT_MS),
          ...(deps.onAvatarError ? { onError: deps.onAvatarError } : {}),
        })
      : new Map<string, string>();
  const encode: EncodeOptions = {
    canvas: { width: composition.width, height: composition.height, fps: composition.fps },
    format: composition.format,
    videoKbps: composition.videoKbps,
    audioKbps: composition.audioKbps,
    threads: deps.threads,
    fontFile: deps.fontFile,
    avatars,
  };
  const tracks =
    req.captureVideo && deps.sizes
      ? await normalizeVideoTracks(req.tracks, workDir, { runner: deps.runner, sizes: deps.sizes, timeoutMs: remaining })
      : [...req.tracks];
  const videoTracks = tracks.filter((t) => t.kind === 'video');
  let concatList: string | null = null;
  let segmentCount = 0;

  if (req.captureVideo) {
    const model = buildTimelineModel({
      windowMs: req.windowMs,
      startedAtMs: Date.parse(req.startedAtUtc),
      participants: req.timeline.participants,
      events: req.timeline.events,
      tracks,
    });
    const segments = buildSegments(model, encode.canvas);
    const lines: string[] = [];
    for (const seg of segments) {
      const name = `seg_${String(seg.index).padStart(4, '0')}.${composition.format}`;
      const filterScript = path.join(workDir, `seg_${seg.index}.filter`);
      const output = path.join(workDir, name);
      const cmd = buildSegmentCommand(seg, encode, { filterScript, output });
      await writeFile(filterScript, cmd.filterScript, 'utf-8');
      await deps.runner.run(cmd.args, { timeoutMs: remaining() });
      lines.push(concatLine(output));
    }
    concatList = path.join(workDir, 'segments.txt');
    await writeFile(concatList, `${lines.join('\n')}\n`, 'utf-8');
    segmentCount = segments.length;
  }

  const filterScript = path.join(workDir, 'final.filter');
  const final = buildFinalCommand(
    {
      concatList,
      audio: tracks.filter((t) => t.kind === 'audio').map((t) => ({ file: t.file, startMs: t.startMs })),
      durationMs: req.windowMs,
      format: composition.format,
      audioKbps: composition.audioKbps,
      threads: deps.threads,
    },
    { filterScript, output: req.outputPath },
  );
  await writeFile(filterScript, final.filterScript, 'utf-8');
  await deps.runner.run(final.args, { timeoutMs: remaining() });
  return { segments: segmentCount, videoTrackCount: videoTracks.length };
}

/** Timeline de respaldo (sin stop): participantes "Participant N" por orden de primera pista. */
export function fallbackTimeline(manifest: SessionManifest): Timeline {
  const users: string[] = [];
  for (const t of [...manifest.tracks].sort((a, b) => a.startOffsetMs - b.startOffsetMs)) {
    if (!users.includes(t.userId)) users.push(t.userId);
  }
  return {
    participants: users.map((userId, i) => ({ userId, displayName: `Participant ${i + 1}`, role: 'Attendee', actorType: null })),
    events: [],
  };
}
