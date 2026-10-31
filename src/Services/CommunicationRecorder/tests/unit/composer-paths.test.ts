import { mkdtemp, readFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { composeRecording } from '../../src/compose/composer.js';
import type { FfmpegRunner } from '../../src/compose/ffmpeg-runner.js';

const STARTED = '2026-10-30T15:00:00.000Z';

describe('composeRecording — rutas', () => {
  let base: string;
  beforeEach(async () => {
    base = await mkdtemp(path.join(os.tmpdir(), 'composer-paths-'));
  });
  afterEach(async () => {
    await rm(base, { recursive: true, force: true });
  });

  it('con spool relativo la lista de segmentos lleva rutas absolutas (concat las resuelve contra la lista)', async () => {
    const relativeWorkDir = path.relative(process.cwd(), path.join(base, 'work'));
    expect(path.isAbsolute(relativeWorkDir)).toBe(false);
    const runner: FfmpegRunner = { run: async () => undefined };

    await composeRecording(
      {
        startedAtUtc: STARTED,
        captureVideo: true,
        composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 96, format: 'mp4' },
        timeline: { participants: [{ userId: 'ana', displayName: 'Ana' }], events: [] },
        windowMs: 5000,
        tracks: [{ trackId: 'a', userId: 'ana', kind: 'audio', source: 'camera', file: path.join(base, 'a.webm'), startMs: 0, endMs: 5000 }],
        workDir: relativeWorkDir,
        outputPath: path.join(base, 'out.mp4'),
      },
      { runner, fontFile: '/fonts/DejaVuSans.ttf', threads: 1, timeoutMs: 10_000 },
    );

    const list = await readFile(path.join(base, 'work', 'segments.txt'), 'utf-8');
    const files = list.trim().split('\n').map((line) => line.replace(/^file '(.*)'$/, '$1'));
    expect(files.length).toBeGreaterThan(0);
    for (const file of files) expect(path.isAbsolute(file)).toBe(true);
  });
});
