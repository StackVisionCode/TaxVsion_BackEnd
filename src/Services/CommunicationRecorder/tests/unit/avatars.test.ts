import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { circleAvatarArgs, createAvatarSource, prepareAvatars, type AvatarSource } from '../../src/compose/avatars.js';
import { buildSegmentCommand, type EncodeOptions } from '../../src/compose/filter-graph.js';
import type { VideoSegment } from '../../src/compose/layout.js';
import type { FfmpegRunner } from '../../src/compose/ffmpeg-runner.js';

const FILE_A = '0b8f6c1e-5d0a-4c7e-9f1a-2b3c4d5e6f70';
const FILE_B = '1c9e7d2f-6e1b-4d8f-8a2b-3c4d5e6f7a81';

function response(body: Uint8Array | string, init: { status?: number; type?: string; length?: number } = {}): Response {
  const headers = new Headers();
  if (init.type !== undefined) headers.set('content-type', init.type);
  if (init.length !== undefined) headers.set('content-length', String(init.length));
  return new Response(typeof body === 'string' ? body : Buffer.from(body), { status: init.status ?? 200, headers });
}

describe('createAvatarSource', () => {
  it('sin base configurada no hay fotos', () => {
    expect(createAvatarSource({ baseUrl: '  ', timeoutMs: 1000, maxBytes: 1024 })).toBeNull();
  });

  it('arma la URL con su propia base y solo con un fileId GUID', async () => {
    const urls: string[] = [];
    const source = createAvatarSource({
      baseUrl: 'http://cloudstorage-api:8080/',
      timeoutMs: 1000,
      maxBytes: 1024,
      fetchImpl: (async (url: string) => {
        urls.push(url);
        return response(new Uint8Array([1, 2, 3]), { type: 'image/webp' });
      }) as unknown as typeof fetch,
    }) as AvatarSource;

    expect(await source.fetch(FILE_A)).toEqual(Buffer.from([1, 2, 3]));
    expect(await source.fetch('../../etc/passwd')).toBeNull();
    expect(urls).toEqual([`http://cloudstorage-api:8080/storage/avatars/${FILE_A}`]);
  });

  it('404 = sin foto; no imagen o demasiado grande = error', async () => {
    const make = (r: Response) =>
      createAvatarSource({ baseUrl: 'http://cs', timeoutMs: 1000, maxBytes: 4, fetchImpl: (async () => r) as unknown as typeof fetch }) as AvatarSource;

    expect(await make(response('', { status: 404 })).fetch(FILE_A)).toBeNull();
    await expect(make(response('<html>', { type: 'text/html' })).fetch(FILE_A)).rejects.toThrow('not an image');
    await expect(make(response(new Uint8Array(10), { type: 'image/png', length: 10 })).fetch(FILE_A)).rejects.toThrow('size limit');
    await expect(make(response(new Uint8Array(10), { type: 'image/png' })).fetch(FILE_A)).rejects.toThrow('size limit');
  });
});

describe('prepareAvatars', () => {
  let workDir: string;
  beforeEach(async () => {
    workDir = await mkdtemp(path.join(os.tmpdir(), 'avatars-'));
  });
  afterEach(async () => {
    await rm(workDir, { recursive: true, force: true });
  });

  it('una descarga y un recorte por foto; la que falla deja iniciales', async () => {
    const fetched: string[] = [];
    const runs: string[][] = [];
    const errors: string[] = [];
    const source: AvatarSource = {
      fetch: async (fileId) => {
        fetched.push(fileId);
        if (fileId === FILE_B) throw new Error('timeout');
        return Buffer.from([9]);
      },
    };
    const runner: FfmpegRunner = { run: async (args) => void runs.push([...args]) };

    const map = await prepareAvatars(
      [
        { userId: 'ana', displayName: 'Ana', avatarFileId: FILE_A },
        { userId: 'ana-2', displayName: 'Ana (2nd tab)', avatarFileId: FILE_A.toUpperCase() },
        { userId: 'bob', displayName: 'Bob', avatarFileId: FILE_B },
        { userId: 'cleo', displayName: 'Cleo', avatarFileId: null },
      ],
      workDir,
      { source, runner, timeoutMs: 1000, onError: (fileId) => errors.push(fileId) },
    );

    expect(fetched).toEqual([FILE_A, FILE_B]);
    expect(runs).toHaveLength(1);
    expect(map.get('ana')).toBe(path.join(workDir, 'avatars', `${FILE_A}.png`));
    expect(map.get('ana-2')).toBe(map.get('ana'));
    expect(map.has('bob')).toBe(false);
    expect(map.has('cleo')).toBe(false);
    expect(errors).toEqual([FILE_B]);
  });

  it('recorte circular con alfa en un solo frame', () => {
    const args = circleAvatarArgs('/w/a.img', '/w/a.png', 256);
    expect(args).toEqual([
      '-hide_banner', '-loglevel', 'error', '-y', '-i', '/w/a.img',
      '-vf',
      "scale=256:256:force_original_aspect_ratio=increase,crop=256:256,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='255*lte(hypot(X-127.5,Y-127.5),128)'",
      '-frames:v', '1', '/w/a.png',
    ]);
  });
});

describe('slate con foto', () => {
  const encode: EncodeOptions = {
    canvas: { width: 1280, height: 720, fps: 25 },
    format: 'mp4',
    videoKbps: 1500,
    audioKbps: 128,
    threads: 2,
    fontFile: '/fonts/DejaVuSans.ttf',
    avatars: new Map([['ana', '/w/avatars/a.png']]),
  };
  const seg: VideoSegment = {
    index: 0,
    startFrame: 0,
    endFrame: 250,
    startMs: 0,
    endMs: 10_000,
    layout: 'grid',
    tiles: [
      { kind: 'participant', rect: { x: 8, y: 184, w: 628, h: 352 }, userId: 'ana', displayName: 'Ana', initials: 'AN', clips: [], muted: [], hand: [], speaking: [] },
      { kind: 'participant', rect: { x: 644, y: 184, w: 628, h: 352 }, userId: 'bob', displayName: 'Bob', initials: 'BO', clips: [], muted: [], hand: [], speaking: [] },
    ],
    screen: null,
    toasts: [],
    reactions: [],
  };

  it('quien tiene foto la ve en círculo con su nombre; quien no, iniciales', () => {
    const cmd = buildSegmentCommand(seg, encode, { filterScript: 'f', output: 'o.mp4' });
    const chains = cmd.filterScript.split(';\n');

    expect(cmd.args.slice(cmd.args.indexOf('-i'), cmd.args.indexOf('-i') + 2)).toEqual(['-i', '/w/avatars/a.png']);
    expect(chains).toContain('color=c=0x374151:s=628x352:r=25:d=10.020[t0_bg]');
    expect(chains).toContain('[0:v]scale=176:176,format=rgba[t0_av]');
    expect(chains.find((c) => c.startsWith('[t0_bg][t0_av]overlay=x=226:y=58:eof_action=repeat,drawtext='))).toContain("text='Ana'");
    expect(cmd.filterScript).not.toContain("text='AN'");
    expect(cmd.filterScript).toContain("text='BO'");
  });

  it('sin mapa de fotos el grafo no cambia', () => {
    const { avatars: _unused, ...plain } = encode;
    const cmd = buildSegmentCommand(seg, plain, { filterScript: 'f', output: 'o.mp4' });
    expect(cmd.args).not.toContain('-i');
    expect(cmd.filterScript).toContain("text='AN'");
  });
});
