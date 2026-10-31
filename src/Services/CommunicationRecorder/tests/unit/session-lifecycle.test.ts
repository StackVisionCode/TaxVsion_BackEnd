import { rm } from 'node:fs/promises';
import { afterEach, describe, expect, it } from 'vitest';
import { PortAllocator } from '../../src/capture/port-allocator.js';
import { SessionManager } from '../../src/capture/session-manager.js';
import type { CreateSessionRequest, OpenTrackRequest } from '../../src/contracts/api.js';
import { ComposeQueue } from '../../src/queue/compose-queue.js';
import { KeyedMutex, SpoolStore } from '../../src/spool/spool-store.js';
import { FakeBus, FakeLauncher, InMemoryRedis, makeManifest, silentLogger, tempDir, testMetrics, waitFor } from './helpers.js';

const dirs: string[] = [];
const managers: SessionManager[] = [];
afterEach(async () => {
  while (managers.length > 0) await managers.pop()?.shutdown();
  while (dirs.length > 0) await rm(dirs.pop() as string, { recursive: true, force: true });
});

async function setup() {
  const dir = await tempDir();
  dirs.push(dir);
  const store = new SpoolStore(dir);
  await store.init();
  const redis = new InMemoryRedis();
  const queue = new ComposeQueue(redis, redis, 'recorder');
  const bus = new FakeBus();
  const launcher = new FakeLauncher();
  const ports = new PortAllocator(44000, 44099);
  let ids = 0;
  const sessions = new SessionManager({
    store,
    locks: new KeyedMutex(),
    jobs: queue,
    bus,
    ports,
    launcher,
    metrics: testMetrics(),
    logger: silentLogger,
    settings: {
      announcedIp: null,
      listenIp: '0.0.0.0',
      maxActiveSessions: 4,
      stopTimeoutMs: 50,
      orphanTimeoutMinutes: 30,
      retry: { maxAttempts: 1, backoffMs: [0] },
    },
    newId: () => `id-${(ids += 1)}`,
  });
  managers.push(sessions);
  return { store, redis, queue, bus, launcher, ports, sessions };
}

const createReq = (over: Partial<CreateSessionRequest> = {}): CreateSessionRequest => ({
  sessionId: 's1',
  tenantId: 'tenant',
  scope: 'Call',
  scopeId: 'call-1',
  startedAtUtc: new Date().toISOString(),
  maxDurationSeconds: 3600,
  toleranceSeconds: 60,
  tracksRetentionDays: 7,
  captureVideo: true,
  composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 128, format: 'mp4' },
  ...over,
});

const trackReq = (trackId: string): OpenTrackRequest => ({
  trackId,
  producerId: `p-${trackId}`,
  userId: 'u1',
  kind: 'audio',
  source: 'camera',
  codec: { mimeType: 'audio/opus', payloadType: 100, clockRate: 48000, channels: 2 },
  ssrc: 77,
});

describe('SessionManager', () => {
  it('arranque: capturando → failed + composition_failed RecorderRestarted; encolada sin trabajo → reencola', async () => {
    const { store, redis, queue, bus, sessions } = await setup();
    for (const [id, over] of [
      ['live', { state: 'capturing' as const }],
      ['limit', { state: 'limit_reached' as const }],
      ['queued-lost', { state: 'queued' as const, jobId: 'job-lost' }],
      ['queued-ok', { state: 'queued' as const, jobId: 'job-ok' }],
      ['done', { state: 'composed' as const, jobId: 'job-done' }],
    ] as const) {
      await store.createSessionDir(id);
      await store.writeManifest(makeManifest(id, over));
    }
    await queue.enqueue({ jobId: 'job-ok', sessionId: 'queued-ok', mode: 'compose', enqueuedAtUtc: 'x' });
    // 'job-crashed' quedó en curso cuando cayó el proceso.
    await redis.lpush('recorder:compose:processing', JSON.stringify({ jobId: 'job-crashed', sessionId: 'z', mode: 'compose', enqueuedAtUtc: 'x' }));

    await sessions.recoverOnBoot();

    expect((await store.readManifest('live'))?.state).toBe('failed');
    expect((await store.readManifest('live'))?.failure?.reason).toBe('RecorderRestarted');
    expect((await store.readManifest('limit'))?.state).toBe('failed');
    expect(bus.events.map((e) => [e.body['sessionId'], e.body['reason']]).sort()).toEqual([
      ['limit', 'RecorderRestarted'],
      ['live', 'RecorderRestarted'],
    ]);
    const queued = (await redis.lrange('recorder:compose:queue', 0, -1)).map((r) => (JSON.parse(r) as { jobId: string }).jobId);
    expect(queued.sort()).toEqual(['job-crashed', 'job-lost', 'job-ok']);
    expect(await redis.llen('recorder:compose:processing')).toBe(0);
  });

  it('corte duro: cierra capturas, rechaza pistas y, sin stop, compone con timeline de respaldo', async () => {
    const { store, queue, launcher, sessions, ports } = await setup();
    await sessions.createSession(createReq(), 'corr');
    await sessions.openTrack('s1', trackReq('t1'));
    expect(ports.inUse).toBe(1);
    await sessions.hardStop('s1');
    const m = await store.readManifest('s1');
    expect(m?.state).toBe('limit_reached');
    expect(m?.hardStoppedAtUtc).toBeDefined();
    expect(m?.tracks[0]?.endedOffsetMs).not.toBeNull();
    expect(launcher.handles[0]?.isAlive()).toBe(false);
    await waitFor(() => ports.inUse === 0);
    await expect(sessions.openTrack('s1', trackReq('t2'))).rejects.toMatchObject({ code: 'Recording.SessionClosed' });
    expect(sessions.activeSessions).toBe(1); // sigue esperando el stop

    await sessions.composeOrphan('s1');
    const after = await store.readManifest('s1');
    expect(after?.state).toBe('queued');
    expect(after?.stop).toMatchObject({ stoppedAtUtc: null, reason: 'RecorderOrphan' });
    expect(await store.readTimeline('s1')).toBeNull();
    expect(await queue.contains(after?.jobId ?? '')).toBe(true);
    expect(sessions.activeSessions).toBe(0);
  });

  it('el timer de corte duro dispara si el límite ya venció; stop tras el corte sigue funcionando', async () => {
    const { store, sessions } = await setup();
    await sessions.createSession(createReq({ startedAtUtc: new Date(Date.now() - 10_000).toISOString(), maxDurationSeconds: 5, toleranceSeconds: 1 }), undefined);
    await waitFor(async () => (await store.readManifest('s1'))?.state === 'limit_reached');
    const { jobId } = await sessions.stopSession('s1', { stoppedAtUtc: new Date().toISOString(), reason: 'LimitReached', timeline: { participants: [], events: [] } });
    expect(jobId).toMatch(/^id-/);
    expect((await store.readManifest('s1'))?.state).toBe('queued');
    expect((await store.readManifest('s1'))?.correlationId).toBe('s1');
  });

  it('si ffmpeg muere solo, la pista termina ahí y libera sus puertos', async () => {
    const { store, launcher, sessions, ports } = await setup();
    await sessions.createSession(createReq(), undefined);
    await sessions.openTrack('s1', trackReq('t1'));
    expect(launcher.handles[0]?.spec.rtcp).toEqual({ ip: '127.0.0.1', port: 44001, ssrc: 77 });
    launcher.handles[0]?.exit(1);
    await waitFor(async () => (await store.readManifest('s1'))?.tracks[0]?.endedOffsetMs !== null);
    expect(ports.inUse).toBe(0);
    expect(sessions.activeTracks).toBe(0);
    await expect(sessions.openTrack('s1', trackReq('t1'))).rejects.toMatchObject({ code: 'Recording.TrackClosed' });
  });
});
