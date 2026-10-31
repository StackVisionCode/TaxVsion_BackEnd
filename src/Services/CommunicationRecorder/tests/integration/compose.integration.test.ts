import { execFile } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { createFfmpegCaptureLauncher } from '../../src/capture/ffmpeg-capture.js';
import { buildSdp } from '../../src/capture/sdp.js';
import { composeRecording, resolveInput } from '../../src/compose/composer.js';
import { createFfmpegRunner, createProber } from '../../src/compose/ffmpeg-runner.js';
import type { Timeline } from '../../src/contracts/api.js';
import type { ManifestTrack, SessionManifest } from '../../src/spool/manifest.js';

/**
 * Composición real con ffmpeg (fuera de `npm test`). Requiere:
 *   RECORDER_IT_FFMPEG, RECORDER_IT_FFPROBE, RECORDER_IT_FONT (y opcional RECORDER_IT_KEEP=1 para ver la salida).
 */
const exec = promisify(execFile);
const FFMPEG = process.env['RECORDER_IT_FFMPEG'] ?? 'ffmpeg';
const FFPROBE = process.env['RECORDER_IT_FFPROBE'] ?? 'ffprobe';
const FONT = process.env['RECORDER_IT_FONT'] ?? '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf';
const KEEP = process.env['RECORDER_IT_KEEP'] === '1';

const STARTED = '2026-10-30T15:00:00.000Z';
const at = (ms: number) => new Date(Date.parse(STARTED) + ms).toISOString();

let dir: string;

async function ff(args: string[]): Promise<void> {
  await exec(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', ...args], { maxBuffer: 16 * 1024 * 1024 });
}

async function video(file: string, seconds: number, src: string, size = '1280x720'): Promise<void> {
  await ff([
    '-f', 'lavfi', '-i', `${src}=size=${size}:rate=25`, '-t', String(seconds),
    '-c:v', 'libvpx', '-b:v', '1M', '-deadline', 'realtime', '-cpu-used', '8', '-g', '50', path.join(dir, file),
  ]);
}

async function audio(file: string, seconds: number, freq: number): Promise<void> {
  await ff([
    '-f', 'lavfi', '-i', `sine=frequency=${freq}:sample_rate=48000`, '-t', String(seconds),
    '-ac', '2', '-c:a', 'libopus', '-b:a', '48k', path.join(dir, file),
  ]);
}

function track(p: Partial<ManifestTrack> & Pick<ManifestTrack, 'trackId' | 'userId' | 'kind' | 'source' | 'startOffsetMs'>): ManifestTrack {
  return {
    producerId: `prod-${p.trackId}`,
    codec: { mimeType: p.kind === 'audio' ? 'audio/opus' : 'video/VP8', payloadType: 100, clockRate: p.kind === 'audio' ? 48000 : 90000, channels: 2 },
    ssrc: 1,
    file: `${p.trackId}.webm`,
    rtpPort: 0,
    rtcpPort: 0,
    endedOffsetMs: null,
    ...p,
  };
}

beforeAll(async () => {
  dir = await mkdtemp(path.join(os.tmpdir(), 'recorder-it-'));
});

afterAll(async () => {
  if (!KEEP) await rm(dir, { recursive: true, force: true });
  else console.warn(`integration output kept at ${dir}`);
});

describe('composición real con ffmpeg', () => {
  it('3 participantes (cámara, cámara con cambio de resolución + pantalla, solo audio) → MP4 válido', async () => {
    await video('a-cam.webm', 30, 'testsrc2');
    await audio('a-mic.webm', 30, 440);
    // Cámara de B con cambio de resolución a mitad (capa simulcast): 7 s a 720p + 8 s a 360p.
    await video('b-cam-1.webm', 7, 'testsrc');
    await video('b-cam-2.webm', 8, 'testsrc', '640x360');
    await writeFile(path.join(dir, 'b-list.txt'), "file 'b-cam-1.webm'\nfile 'b-cam-2.webm'\n");
    await ff(['-f', 'concat', '-safe', '0', '-i', path.join(dir, 'b-list.txt'), '-c', 'copy', path.join(dir, 'b-cam.webm')]);
    await audio('b-mic.webm', 15, 660);
    await video('b-screen.webm', 6, 'smptebars');
    await audio('c-mic.webm', 20, 880);

    const manifest: SessionManifest = {
      version: 1,
      sessionId: 'it-session',
      tenantId: 'tenant',
      scope: 'Meeting',
      scopeId: 'meeting-1',
      startedAtUtc: STARTED,
      maxDurationSeconds: 3600,
      toleranceSeconds: 120,
      tracksRetentionDays: 7,
      captureVideo: true,
      composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 128, format: 'mp4' },
      correlationId: 'it',
      createdAtUtc: STARTED,
      state: 'queued',
      tracks: [
        track({ trackId: 'a-cam', userId: 'ana', kind: 'video', source: 'camera', startOffsetMs: 0 }),
        track({ trackId: 'a-mic', userId: 'ana', kind: 'audio', source: 'camera', startOffsetMs: 0 }),
        track({ trackId: 'b-cam', userId: 'bob', kind: 'video', source: 'camera', startOffsetMs: 3000, endedOffsetMs: 18000 }),
        track({ trackId: 'b-mic', userId: 'bob', kind: 'audio', source: 'camera', startOffsetMs: 3000, endedOffsetMs: 18000 }),
        track({ trackId: 'b-screen', userId: 'bob', kind: 'video', source: 'screen', startOffsetMs: 8000, endedOffsetMs: 14000 }),
        track({ trackId: 'c-mic', userId: 'cleo', kind: 'audio', source: 'camera', startOffsetMs: 5000 }),
      ],
      stop: { stoppedAtUtc: at(30_000), reason: 'User', receivedAtUtc: at(30_000) },
      jobId: 'job-1',
      updatedAtUtc: STARTED,
    };
    const timeline: Timeline = {
      participants: [
        { userId: 'ana', displayName: "Ana O'Brien: test, [x]; 50%", role: 'Host', actorType: 'Staff' },
        { userId: 'bob', displayName: 'Bob Stone', role: 'Attendee', actorType: 'Staff' },
        { userId: 'cleo', displayName: 'Cleo Díaz', role: 'Attendee', actorType: 'Customer' },
      ],
      events: [
        { seq: 1, at: at(-60_000), type: 'participant.joined', actorUserId: 'ana', targetUserId: null, payload: null },
        { seq: 2, at: at(3000), type: 'participant.joined', actorUserId: 'bob', targetUserId: null, payload: null },
        { seq: 3, at: at(5000), type: 'participant.joined', actorUserId: 'cleo', targetUserId: null, payload: null },
        { seq: 4, at: at(6000), type: 'hand.raised', actorUserId: 'cleo', targetUserId: null, payload: null },
        { seq: 5, at: at(4000), type: 'speaker.started', actorUserId: 'ana', targetUserId: null, payload: null },
        { seq: 6, at: at(9000), type: 'speaker.stopped', actorUserId: 'ana', targetUserId: null, payload: null },
        { seq: 7, at: at(8000), type: 'screen.started', actorUserId: 'bob', targetUserId: null, payload: null },
        { seq: 8, at: at(10_000), type: 'media.changed', actorUserId: 'ana', targetUserId: null, payload: { audioEnabled: false } },
        { seq: 9, at: at(14_000), type: 'screen.stopped', actorUserId: 'bob', targetUserId: null, payload: null },
        { seq: 10, at: at(15_000), type: 'media.changed', actorUserId: 'bob', targetUserId: null, payload: { videoEnabled: false } },
        { seq: 11, at: at(16_000), type: 'host.transferred', actorUserId: 'ana', targetUserId: 'bob', payload: null },
        { seq: 12, at: at(18_000), type: 'participant.left', actorUserId: 'bob', targetUserId: null, payload: null },
        { seq: 13, at: at(20_000), type: 'hand.lowered', actorUserId: 'cleo', targetUserId: null, payload: null },
        { seq: 14, at: at(60_000), type: 'reaction.summary', actorUserId: null, targetUserId: null, payload: { minuteUtc: STARTED, counts: { '👍': 2, '🎉': 1 } } },
      ],
    };

    const probe = createProber(FFPROBE);
    const resolved = await resolveInput(manifest, (f) => path.join(dir, f), async (f) => (await stat(f)).size, probe);
    expect(resolved.tracks).toHaveLength(6);
    expect(resolved.windowMs).toBe(30_000);

    const outputPath = path.join(dir, 'composed.mp4');
    await mkdir(path.join(dir, 'work'), { recursive: true });
    const t0 = Date.now();
    const outcome = await composeRecording(
      {
        startedAtUtc: STARTED,
        captureVideo: true,
        composition: manifest.composition,
        timeline,
        windowMs: resolved.windowMs,
        tracks: resolved.tracks,
        workDir: path.join(dir, 'work'),
        outputPath,
      },
      { runner: createFfmpegRunner({ ffmpegPath: FFMPEG, nice: 0 }), fontFile: FONT, threads: 2, timeoutMs: 300_000 },
    );
    const wallMs = Date.now() - t0;

    const { stdout } = await exec(FFPROBE, [
      '-v', 'error', '-show_entries', 'format=duration,size:stream=codec_name,codec_type,width,height,nb_frames,avg_frame_rate',
      '-of', 'json', outputPath,
    ]);
    const info = JSON.parse(stdout) as {
      format: { duration: string; size: string };
      streams: { codec_name: string; codec_type: string; width?: number; height?: number; nb_frames?: string }[];
    };
    const v = info.streams.find((s) => s.codec_type === 'video');
    const a = info.streams.find((s) => s.codec_type === 'audio');
    console.warn(
      `IT compose: segments=${outcome.segments} duration=${info.format.duration}s wall=${(wallMs / 1000).toFixed(2)}s ` +
        `factor=${(30 / (wallMs / 1000)).toFixed(1)}x size=${info.format.size}B video=${v?.codec_name} ${v?.width}x${v?.height} ` +
        `frames=${v?.nb_frames} audio=${a?.codec_name}`,
    );
    expect(v?.codec_name).toBe('h264');
    expect(v?.width).toBe(1280);
    expect(v?.height).toBe(720);
    expect(a?.codec_name).toBe('aac');
    expect(Math.abs(Number(info.format.duration) - 30)).toBeLessThan(0.2);
    expect(Number(v?.nb_frames)).toBe(750);
    expect(outcome.segments).toBeGreaterThanOrEqual(4);

    if (KEEP) {
      for (const s of [2, 6, 11, 16, 25]) {
        await ff(['-ss', String(s), '-i', outputPath, '-frames:v', '1', path.join(dir, `frame-${s}s.png`)]);
      }
    }
  });

  it('cámara apagada: foto WebP en círculo para quien la tiene, iniciales para quien no', async () => {
    const avatarId = '0b8f6c1e-5d0a-4c7e-9f1a-2b3c4d5e6f70';
    const webp = path.join(dir, 'avatar.webp');
    await ff(['-f', 'lavfi', '-i', 'testsrc2=size=512x512:rate=1', '-frames:v', '1', '-c:v', 'libwebp', webp]);
    await audio('p-ana.webm', 10, 440);
    await audio('p-bob.webm', 10, 660);
    const fetched: string[] = [];
    const outputPath = path.join(dir, 'photo-slate.mp4');
    const workDir = path.join(dir, 'work-photo');

    await composeRecording(
      {
        startedAtUtc: STARTED,
        captureVideo: true,
        composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 96, format: 'mp4' },
        timeline: {
          participants: [
            { userId: 'ana', displayName: 'Ana Photo', role: 'Host', actorType: 'Staff', avatarFileId: avatarId },
            { userId: 'bob', displayName: 'Bob Initials', role: 'Attendee', actorType: 'Staff', avatarFileId: null },
          ],
          events: [
            { seq: 1, at: at(0), type: 'participant.joined', actorUserId: 'ana', targetUserId: null, payload: null },
            { seq: 2, at: at(0), type: 'participant.joined', actorUserId: 'bob', targetUserId: null, payload: null },
          ],
        },
        windowMs: 10_000,
        tracks: [
          { trackId: 'p-ana', userId: 'ana', kind: 'audio', source: 'camera', file: path.join(dir, 'p-ana.webm'), startMs: 0, endMs: 10_000 },
          { trackId: 'p-bob', userId: 'bob', kind: 'audio', source: 'camera', file: path.join(dir, 'p-bob.webm'), startMs: 0, endMs: 10_000 },
        ],
        workDir,
        outputPath,
      },
      {
        runner: createFfmpegRunner({ ffmpegPath: FFMPEG, nice: 0 }),
        fontFile: FONT,
        threads: 2,
        timeoutMs: 120_000,
        avatars: {
          fetch: async (fileId) => {
            fetched.push(fileId);
            return readFile(webp);
          },
        },
      },
    );

    expect(fetched).toEqual([avatarId]);
    expect((await stat(path.join(workDir, 'avatars', `${avatarId}.png`))).size).toBeGreaterThan(0);
    const { stdout } = await exec(FFPROBE, ['-v', 'error', '-show_entries', 'format=duration:stream=codec_type,codec_name,width,height', '-of', 'json', outputPath]);
    const info = JSON.parse(stdout) as { format: { duration: string }; streams: { codec_type: string; codec_name: string; width?: number }[] };
    expect(info.streams.map((st) => st.codec_name)).toEqual(['h264', 'aac']);
    expect(Math.abs(Number(info.format.duration) - 10)).toBeLessThan(0.3);
    // Fotograma para revisar a ojo con RECORDER_IT_KEEP=1.
    await ff(['-ss', '5', '-i', outputPath, '-frames:v', '1', path.join(dir, 'photo-slate.png')]);
  });

  it('captureVideo=false → solo audio AAC en MP4', async () => {
    const manifest = {
      startedAtUtc: STARTED,
      captureVideo: false,
      composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 96, format: 'mp4' as const },
    };
    const outputPath = path.join(dir, 'audio-only.mp4');
    await composeRecording(
      {
        ...manifest,
        timeline: { participants: [], events: [] },
        windowMs: 20_000,
        tracks: [
          { trackId: 'a-mic', userId: 'ana', kind: 'audio', source: 'camera', file: path.join(dir, 'a-mic.webm'), startMs: 0, endMs: 20_000 },
          { trackId: 'c-mic', userId: 'cleo', kind: 'audio', source: 'camera', file: path.join(dir, 'c-mic.webm'), startMs: 5000, endMs: 20_000 },
        ],
        workDir: path.join(dir, 'work-audio'),
        outputPath,
      },
      { runner: createFfmpegRunner({ ffmpegPath: FFMPEG, nice: 0 }), fontFile: FONT, threads: 2, timeoutMs: 120_000 },
    );
    const { stdout } = await exec(FFPROBE, ['-v', 'error', '-show_entries', 'format=duration:stream=codec_type,codec_name', '-of', 'json', outputPath]);
    const info = JSON.parse(stdout) as { format: { duration: string }; streams: { codec_type: string; codec_name: string }[] };
    expect(info.streams).toEqual([{ codec_type: 'audio', codec_name: 'aac' }]);
    expect(Math.abs(Number(info.format.duration) - 20)).toBeLessThan(0.2);
  });

  it('format=webm → VP8 + Opus', async () => {
    const outputPath = path.join(dir, 'composed.webm');
    const t0 = Date.now();
    const outcome = await composeRecording(
      {
        startedAtUtc: STARTED,
        captureVideo: true,
        composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 96, format: 'webm' },
        timeline: { participants: [{ userId: 'ana', displayName: 'Ana' }], events: [] },
        windowMs: 12_000,
        tracks: [
          { trackId: 'a-cam', userId: 'ana', kind: 'video', source: 'camera', file: path.join(dir, 'a-cam.webm'), startMs: 0, endMs: 12_000 },
          { trackId: 'a-mic', userId: 'ana', kind: 'audio', source: 'camera', file: path.join(dir, 'a-mic.webm'), startMs: 0, endMs: 12_000 },
          { trackId: 'c-mic', userId: 'cleo', kind: 'audio', source: 'camera', file: path.join(dir, 'c-mic.webm'), startMs: 4000, endMs: 12_000 },
        ],
        workDir: path.join(dir, 'work-webm'),
        outputPath,
      },
      { runner: createFfmpegRunner({ ffmpegPath: FFMPEG, nice: 0 }), fontFile: FONT, threads: 2, timeoutMs: 300_000 },
    );
    const wallMs = Date.now() - t0;
    const { stdout } = await exec(FFPROBE, ['-v', 'error', '-show_entries', 'format=duration,size:stream=codec_type,codec_name', '-of', 'json', outputPath]);
    const info = JSON.parse(stdout) as { format: { duration: string; size: string }; streams: { codec_type: string; codec_name: string }[] };
    console.warn(`IT webm: segments=${outcome.segments} duration=${info.format.duration}s wall=${(wallMs / 1000).toFixed(2)}s size=${info.format.size}B`);
    expect(info.streams.map((s) => s.codec_name)).toEqual(['vp8', 'opus']);
    expect(Math.abs(Number(info.format.duration) - 12)).toBeLessThan(0.2);
  });

  it('captura RTP → WebM con ffmpeg -c copy y cierre ordenado', async () => {
    const port = 46_000 + Math.floor(Math.random() * 400) * 2;
    const sdpPath = path.join(dir, 'cap.sdp');
    await writeFile(
      sdpPath,
      buildSdp({ listenIp: '127.0.0.1', kind: 'audio', rtpPort: port, rtcpPort: port + 1, codec: { mimeType: 'audio/opus', payloadType: 101, clockRate: 48000, channels: 2 }, ssrc: 1111 }),
    );
    const launcher = createFfmpegCaptureLauncher({ ffmpegPath: FFMPEG, readyDelayMs: 400 });
    const handle = await launcher.launch({ sdpPath, outputPath: path.join(dir, 'cap.webm'), logPath: path.join(dir, 'cap.log'), rtcp: { ip: '127.0.0.1', port: port + 1, ssrc: 1111 } });
    expect(handle.isAlive()).toBe(true);
    await ff([
      '-re', '-f', 'lavfi', '-i', 'sine=frequency=500:sample_rate=48000', '-t', '3',
      '-c:a', 'libopus', '-ac', '2', '-b:a', '48k', '-ssrc', '1111', '-payload_type', '101', '-f', 'rtp', `rtp://127.0.0.1:${port}?pkt_size=1200`,
    ]);
    await handle.stop(5000);
    expect(handle.isAlive()).toBe(false);
    const probe = await createProber(FFPROBE)(path.join(dir, 'cap.webm'));
    expect(probe.streams.map((s) => s.codecType)).toEqual(['audio']);
    expect(probe.durationMs ?? 0).toBeGreaterThan(2000);
  });
});
