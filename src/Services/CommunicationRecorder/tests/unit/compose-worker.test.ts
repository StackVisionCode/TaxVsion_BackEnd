import { existsSync } from 'node:fs';
import { rm, writeFile } from 'node:fs/promises';
import { afterEach, describe, expect, it } from 'vitest';
import type { FfmpegRunner, Prober } from '../../src/compose/ffmpeg-runner.js';
import { ComposeWorker } from '../../src/queue/compose-worker.js';
import type { ManifestTrack, SessionManifest } from '../../src/spool/manifest.js';
import { KeyedMutex, SpoolStore } from '../../src/spool/spool-store.js';
import type { ObjectUploader } from '../../src/upload/object-uploader.js';
import { FakeBus, makeManifest, silentLogger, tempDir, testMetrics } from './helpers.js';

const dirs: string[] = [];
afterEach(async () => {
  while (dirs.length > 0) await rm(dirs.pop() as string, { recursive: true, force: true });
});

const STARTED = '2026-10-30T15:00:00.000Z';
const NOW = new Date('2026-10-30T16:00:00.000Z');

function mt(trackId: string, userId: string, kind: 'audio' | 'video', startOffsetMs: number, endedOffsetMs: number | null): ManifestTrack {
  return {
    trackId,
    producerId: `p-${trackId}`,
    userId,
    kind,
    source: 'camera',
    codec: { mimeType: kind === 'audio' ? 'audio/opus' : 'video/VP8', payloadType: 100, clockRate: 48000 },
    ssrc: 1,
    file: `${trackId}.webm`,
    rtpPort: 44000,
    rtcpPort: 44001,
    startOffsetMs,
    endedOffsetMs,
  };
}

class FakeUploader implements ObjectUploader {
  readonly bucket = 'taxvision-temp';
  readonly puts: { objectKey: string; filePath: string; contentType: string }[] = [];
  failures: unknown[] = [];

  async putFile(objectKey: string, filePath: string, contentType: string): Promise<void> {
    const next = this.failures.shift();
    if (next) throw next;
    this.puts.push({ objectKey, filePath, contentType });
  }
}

async function setup(over: Partial<SessionManifest> = {}, withFiles = true) {
  const dir = await tempDir();
  dirs.push(dir);
  const store = new SpoolStore(dir);
  await store.init();
  const manifest = makeManifest('sess', {
    scope: 'Meeting',
    scopeId: 'meeting-9',
    tenantId: 'tenant-9',
    startedAtUtc: STARTED,
    state: 'queued',
    jobId: 'job-1',
    correlationId: 'corr-9',
    stop: { stoppedAtUtc: '2026-10-30T15:00:30.000Z', reason: 'User', receivedAtUtc: STARTED },
    tracks: [mt('a1', 'ana', 'audio', 0, 30_000), mt('v1', 'ana', 'video', 0, 30_000), mt('a2', 'bob', 'audio', 4000, 20_000)],
    ...over,
  });
  await store.createSessionDir('sess');
  await store.writeManifest(manifest);
  await store.writeTimeline('sess', { participants: [{ userId: 'ana', displayName: 'Ana' }, { userId: 'bob', displayName: 'Bob' }], events: [] });
  if (withFiles) for (const t of manifest.tracks) await writeFile(store.trackPath('sess', t.file), `data-${t.trackId}`);

  const runs: string[][] = [];
  const runner: FfmpegRunner = {
    async run(args) {
      runs.push([...args]);
      await writeFile(args[args.length - 1] as string, 'media-bytes');
    },
  };
  const probe: Prober = async (file) => {
    if (file.includes('composed-')) return { durationMs: 30_000, streams: [{ codecType: 'video' }, { codecType: 'audio' }] };
    const kind = /[\\/]v\d\.webm$/.test(file) ? 'video' : 'audio';
    return { durationMs: kind === 'audio' && file.includes('a2') ? 15_800 : 30_000, streams: [{ codecType: kind }] };
  };
  const bus = new FakeBus();
  const uploader = new FakeUploader();
  const metrics = testMetrics();
  let ids = 0;
  const worker = new ComposeWorker({
    store,
    locks: new KeyedMutex(),
    queue: { take: async () => null, ack: async () => undefined, requeue: async () => undefined },
    bus,
    uploader,
    runner,
    probe,
    metrics,
    logger: silentLogger,
    settings: {
      fontFile: '/fonts/DejaVuSans.ttf',
      threads: 2,
      timeoutFactor: 3,
      timeoutMinMinutes: 10,
      sourcePrefix: 'recording',
      externalUploadsQueue: 'cloudstorage-external-uploads',
      retry: { maxAttempts: 3, backoffMs: [0] },
    },
    now: () => NOW,
    newId: () => `id-${(ids += 1)}`,
    sleep: async () => undefined,
  });
  const job = { jobId: 'job-1', sessionId: 'sess', mode: 'compose' as const, enqueuedAtUtc: STARTED };
  return { store, worker, bus, uploader, runs, metrics, job };
}

describe('ComposeWorker', () => {
  it('compone, sube pistas de audio y luego el compuesto, publica composed.v1 con el contrato', async () => {
    const { store, worker, bus, uploader, runs, job } = await setup();
    expect(await worker.processJob(job)).toBe('done');

    // 1 segmento (mismos ocupantes toda la ventana) + mux final.
    expect(runs).toHaveLength(2);
    expect(uploader.puts.map((p) => [p.objectKey, p.contentType])).toEqual([
      ['recording/tenant-9/sess/id-1.webm', 'audio/webm'],
      ['recording/tenant-9/sess/id-3.webm', 'audio/webm'],
      ['recording/tenant-9/sess/id-5.mp4', 'video/mp4'],
    ]);
    expect(bus.queued.map((q) => q.queue)).toEqual(Array(3).fill('cloudstorage-external-uploads'));
    expect(bus.queued[0]?.body).toEqual({
      EventId: 'id-2',
      TenantId: 'tenant-9',
      OccurredOn: NOW.toISOString(),
      CorrelationId: 'corr-9',
      FileId: 'id-1',
      RequestingService: 'communication-recorder',
      SourceBucket: 'taxvision-temp',
      SourceObjectKey: 'recording/tenant-9/sess/id-1.webm',
      ActorId: '00000000-0000-0000-0000-000000000000',
      OwnerType: 'Communication',
      OwnerId: 'meeting-9',
      FolderType: 'RecordingTracks',
      TaxYear: null,
      OriginalName: 'Track_ana_a1.webm',
      ContentType: 'audio/webm',
      SizeBytes: 'data-a1'.length,
    });
    expect(bus.queued[2]?.body).toMatchObject({
      FileId: 'id-5',
      FolderType: 'Recordings',
      OriginalName: 'Meeting_Recording_2026-10-30.mp4',
      ContentType: 'video/mp4',
      SizeBytes: 'media-bytes'.length,
    });

    expect(bus.events).toHaveLength(1);
    const event = bus.events[0];
    expect(event?.eventType).toBe('communication.recording.composed.v1');
    expect(event?.eventId).toBe(event?.body['eventId']);
    expect(event?.body).toEqual({
      eventId: 'id-7',
      eventType: 'communication.recording.composed.v1',
      tenantId: 'tenant-9',
      correlationId: 'corr-9',
      occurredOnUtc: NOW.toISOString(),
      sessionId: 'sess',
      scope: 'Meeting',
      scopeId: 'meeting-9',
      composedFileId: 'id-5',
      composedSizeBytes: 'media-bytes'.length,
      format: 'mp4',
      durationSeconds: 30,
      layout: 'auto',
      audioTracks: [
        { trackId: 'a1', userId: 'ana', fileId: 'id-1', source: 'camera', startOffsetMs: 0, durationMs: 30_000 },
        { trackId: 'a2', userId: 'bob', fileId: 'id-3', source: 'camera', startOffsetMs: 4000, durationMs: 15_800 },
      ],
      videoTrackCount: 1,
      composedAtUtc: NOW.toISOString(),
    });

    const m = await store.readManifest('sess');
    expect(m?.state).toBe('composed');
    expect(m?.retainUntilUtc).toBe('2026-11-06T16:00:00.000Z');
    expect(m?.tracks.find((t) => t.trackId === 'a1')?.fileId).toBe('id-1');
    expect(existsSync(store.trackPath('sess', 'composed-job-1.mp4'))).toBe(false);
    expect(existsSync(store.trackPath('sess', 'compose-job-1'))).toBe(false);
    // El spool (pistas) se conserva para recomponer.
    expect(existsSync(store.trackPath('sess', 'v1.webm'))).toBe(true);
  });

  it('recomposición: no vuelve a subir las pistas de audio ya registradas', async () => {
    const { worker, uploader, bus, job } = await setup({
      tracks: [{ ...mt('a1', 'ana', 'audio', 0, 30_000), fileId: 'existing-1', durationMs: 29_900 }],
      result: { jobId: 'old', composedFileId: 'old-file', composedSizeBytes: 1, format: 'mp4', durationSeconds: 30, composedAtUtc: STARTED, videoTrackCount: 0 },
      composition: { width: 640, height: 360, fps: 15, videoKbps: 600, audioKbps: 64, format: 'webm' },
    });
    expect(await worker.processJob(job)).toBe('done');
    expect(uploader.puts.map((p) => p.contentType)).toEqual(['video/webm']);
    expect(bus.events[0]?.body['audioTracks']).toEqual([
      { trackId: 'a1', userId: 'ana', fileId: 'existing-1', source: 'camera', startOffsetMs: 0, durationMs: 29_900 },
    ]);
    expect(bus.events[0]?.body['format']).toBe('webm');
  });

  it('sin pistas utilizables → composition_failed NoTracks', async () => {
    const { store, worker, bus, metrics, job } = await setup({}, false);
    expect(await worker.processJob(job)).toBe('done');
    expect(bus.events.map((e) => [e.eventType, e.body['reason']])).toEqual([['communication.recording.composition_failed.v1', 'NoTracks']]);
    expect(bus.events[0]?.body).toMatchObject({ sessionId: 'sess', scope: 'Meeting', scopeId: 'meeting-9', tenantId: 'tenant-9', correlationId: 'corr-9' });
    const m = await store.readManifest('sess');
    expect(m?.state).toBe('failed');
    expect(m?.failure?.reason).toBe('NoTracks');
    expect(await metrics.failuresTotal.get()).toMatchObject({ values: [{ labels: { reason: 'NoTracks' }, value: 1 }] });
  });

  it('ffmpeg falla → ComposeError; subida no transitoria → UploadFailed; transitoria se reintenta', async () => {
    const a = await setup();
    const failing: FfmpegRunner = { run: async () => Promise.reject(new Error('ffmpeg exited with 1: boom')) };
    (a.worker as unknown as { deps: { runner: FfmpegRunner } }).deps.runner = failing;
    expect(await a.worker.processJob(a.job)).toBe('done');
    expect(a.bus.events[0]?.body).toMatchObject({ reason: 'ComposeError', errorMessage: 'ffmpeg exited with 1: boom' });

    const b = await setup();
    b.uploader.failures = [Object.assign(new Error('Access Denied'), { code: 'AccessDenied' })];
    expect(await b.worker.processJob(b.job)).toBe('done');
    expect(b.bus.events[0]?.body['reason']).toBe('UploadFailed');

    const c = await setup();
    c.uploader.failures = [Object.assign(new Error('reset'), { code: 'ECONNRESET' })];
    expect(await c.worker.processJob(c.job)).toBe('done');
    expect(c.bus.events[0]?.eventType).toBe('communication.recording.composed.v1');
    expect(c.uploader.puts).toHaveLength(3);
  });

  it('si el evento final no se publica: retry sin recomponer ni resubir en el siguiente intento', async () => {
    const { store, worker, bus, uploader, runs, job } = await setup();
    bus.failEvents = 3;
    expect(await worker.processJob(job)).toBe('retry');
    expect((await store.readManifest('sess'))?.result?.composedFileId).toBe('id-5');
    expect(runs).toHaveLength(2);
    expect(await worker.processJob(job)).toBe('done');
    expect(runs).toHaveLength(2);
    expect(uploader.puts).toHaveLength(3);
    expect(bus.events[0]?.body['composedFileId']).toBe('id-5');
  });

  it('trabajo de una sesión abortada, reemplazada o inexistente se descarta sin eventos', async () => {
    const { store, worker, bus, job } = await setup({ state: 'aborted' });
    expect(await worker.processJob(job)).toBe('done');
    const m = await store.readManifest('sess');
    if (!m) throw new Error('manifest');
    m.state = 'queued';
    m.jobId = 'job-2';
    await store.writeManifest(m);
    expect(await worker.processJob(job)).toBe('done');
    expect(await worker.processJob({ ...job, sessionId: 'ghost' })).toBe('done');
    expect(bus.events).toHaveLength(0);
  });

  it('sin timeline (huérfana) compone con "Participant N"', async () => {
    const { store, worker, runs, job } = await setup({ stop: { stoppedAtUtc: null, reason: 'RecorderOrphan', receivedAtUtc: STARTED }, hardStoppedAtUtc: '2026-10-30T15:00:25.000Z' });
    await rm(store.trackPath('sess', 'timeline.json'));
    expect(await worker.processJob(job)).toBe('done');
    const finalArgs = runs[runs.length - 1] ?? [];
    expect(finalArgs[finalArgs.indexOf('-t') + 1]).toBe('25.000');
  });
});
