import { readFile, rm } from 'node:fs/promises';
import path from 'node:path';
import type { FastifyInstance } from 'fastify';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { CaptureStartError } from '../../src/capture/ffmpeg-capture.js';
import { PortAllocator } from '../../src/capture/port-allocator.js';
import { SessionManager } from '../../src/capture/session-manager.js';
import { buildServer } from '../../src/http/server.js';
import { createServiceAuthGuard } from '../../src/http/service-auth.js';
import { ComposeQueue } from '../../src/queue/compose-queue.js';
import { KeyedMutex, SpoolStore } from '../../src/spool/spool-store.js';
import {
  AUDIENCE,
  FakeBus,
  FakeLauncher,
  ISSUER,
  InMemoryRedis,
  serviceClaims,
  silentLogger,
  tempDir,
  testKeys,
  testMetrics,
  waitFor,
  type TestKeys,
} from './helpers.js';

let keys: TestKeys;
let token: string;
const cleanups: (() => Promise<void>)[] = [];

beforeAll(async () => {
  keys = await testKeys();
  token = await keys.sign(serviceClaims());
});

afterEach(async () => {
  while (cleanups.length > 0) await cleanups.pop()?.();
});

const START = new Date(Date.now() - 1000).toISOString();

function sessionBody(over: Record<string, unknown> = {}) {
  return {
    sessionId: 'sess-1',
    tenantId: 'tenant-1',
    scope: 'Meeting',
    scopeId: 'meeting-1',
    startedAtUtc: START,
    maxDurationSeconds: 3600,
    toleranceSeconds: 120,
    tracksRetentionDays: 7,
    captureVideo: true,
    composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 128, format: 'mp4' },
    ...over,
  };
}

function trackBody(over: Record<string, unknown> = {}) {
  return {
    trackId: 'track-a',
    producerId: 'producer-a',
    userId: 'user-a',
    kind: 'audio',
    source: 'camera',
    codec: { mimeType: 'audio/opus', payloadType: 100, clockRate: 48000, channels: 2 },
    ssrc: 1234,
    ...over,
  };
}

const timeline = { participants: [{ userId: 'user-a', displayName: 'Ana', role: 'Host', actorType: 'Staff' }], events: [] };

async function setup(opts: { maxActiveSessions?: number; announcedIp?: string | null } = {}) {
  const dir = await tempDir();
  const store = new SpoolStore(dir);
  await store.init();
  const redis = new InMemoryRedis();
  const queue = new ComposeQueue(redis, redis, 'recorder');
  const bus = new FakeBus();
  const launcher = new FakeLauncher();
  const metrics = testMetrics();
  const ports = new PortAllocator(44000, 44009);
  const sessions = new SessionManager({
    store,
    locks: new KeyedMutex(),
    jobs: queue,
    bus,
    ports,
    launcher,
    metrics,
    logger: silentLogger,
    settings: {
      announcedIp: opts.announcedIp ?? null,
      listenIp: '127.0.0.1',
      maxActiveSessions: opts.maxActiveSessions ?? 4,
      stopTimeoutMs: 100,
      orphanTimeoutMinutes: 30,
      retry: { maxAttempts: 1, backoffMs: [0] },
    },
  });
  const app: FastifyInstance = await buildServer({
    sessions,
    auth: createServiceAuthGuard({ keys: keys.jwks, issuer: ISSUER, audience: AUDIENCE, allowedClientIds: ['communication-worker'] }),
    metrics,
    queueDepth: () => queue.depth(),
    logger: silentLogger,
    bodyLimitBytes: 1024 * 1024,
  });
  cleanups.push(async () => {
    await sessions.shutdown();
    await app.close();
    await rm(dir, { recursive: true, force: true });
  });
  const call = (method: 'GET' | 'POST' | 'DELETE', url: string, payload?: unknown, auth = true) =>
    app.inject({
      method,
      url,
      ...(payload === undefined ? {} : { payload: payload as Record<string, unknown> }),
      headers: auth ? { authorization: `Bearer ${token}`, 'x-correlation-id': 'corr-1' } : {},
    });
  return { app, store, redis, queue, bus, launcher, sessions, ports, call };
}

describe('HTTP API v1', () => {
  it('/health sin auth con contadores', async () => {
    const { call } = await setup();
    const res = await call('GET', '/health', undefined, false);
    expect(res.statusCode).toBe(200);
    expect(res.json()).toEqual({ status: 'ok', activeSessions: 0, activeTracks: 0, queueDepth: 0 });
  });

  it('/v1 exige JWT de servicio (401 / 403)', async () => {
    const { app } = await setup();
    const noAuth = await app.inject({ method: 'POST', url: '/v1/sessions', payload: sessionBody() });
    expect(noAuth.statusCode).toBe(401);
    expect(noAuth.json()).toEqual({ code: 'Auth.InvalidToken', message: expect.any(String) });
    const other = await keys.sign(serviceClaims('other-service'));
    const forbidden = await app.inject({ method: 'POST', url: '/v1/sessions', payload: sessionBody(), headers: { authorization: `Bearer ${other}` } });
    expect(forbidden.statusCode).toBe(403);
    expect(forbidden.json().code).toBe('Auth.ClientNotAllowed');
  });

  it('ciclo completo: crear (201/200), pista (201/200), cerrar, stop (202 idempotente) → trabajo en cola', async () => {
    const { call, launcher, queue, store, redis } = await setup({ announcedIp: '10.0.0.9' });
    expect((await call('POST', '/v1/sessions', sessionBody())).statusCode).toBe(201);
    const again = await call('POST', '/v1/sessions', sessionBody());
    expect(again.statusCode).toBe(200);
    expect(again.json()).toEqual({ sessionId: 'sess-1' });

    const t1 = await call('POST', '/v1/sessions/sess-1/tracks', trackBody());
    expect(t1.statusCode).toBe(201);
    expect(t1.json()).toEqual({ ip: '10.0.0.9', port: 44000, rtcpPort: 44001 });
    const dup = await call('POST', '/v1/sessions/sess-1/tracks', trackBody());
    expect(dup.statusCode).toBe(200);
    expect(dup.json()).toEqual({ ip: '10.0.0.9', port: 44000, rtcpPort: 44001 });
    expect(launcher.handles).toHaveLength(1);
    const sdp = await readFile(path.join(store.sessionDir('sess-1'), 'track-a.sdp'), 'utf-8');
    expect(sdp).toContain('m=audio 44000 RTP/AVP 100');
    expect(launcher.handles[0]?.spec.rtcp).toEqual({ ip: '127.0.0.1', port: 44001, ssrc: 1234 });

    const video = await call('POST', '/v1/sessions/sess-1/tracks', trackBody({ trackId: 'track-v', kind: 'video', codec: { mimeType: 'video/VP8', payloadType: 101, clockRate: 90000 } }));
    expect(video.statusCode).toBe(201);
    expect(video.json().port).toBe(44002);

    expect((await call('DELETE', '/v1/sessions/sess-1/tracks/track-v')).statusCode).toBe(204);
    expect((await call('DELETE', '/v1/sessions/sess-1/tracks/track-v')).statusCode).toBe(204);
    expect((await call('DELETE', '/v1/sessions/nope/tracks/x')).statusCode).toBe(204);

    const health = await call('GET', '/health', undefined, false);
    expect(health.json().activeSessions).toBe(1);

    const stop = await call('POST', '/v1/sessions/sess-1/stop', { stoppedAtUtc: new Date().toISOString(), reason: 'User', timeline });
    expect(stop.statusCode).toBe(202);
    const { jobId } = stop.json() as { jobId: string };
    const stop2 = await call('POST', '/v1/sessions/sess-1/stop', { stoppedAtUtc: new Date().toISOString(), reason: 'User', timeline });
    expect(stop2.statusCode).toBe(202);
    expect(stop2.json()).toEqual({ jobId });

    await waitFor(async () => (await queue.depth()) === 1);
    const raw = (await redis.lrange('recorder:compose:queue', 0, -1))[0] ?? '';
    expect(JSON.parse(raw)).toMatchObject({ jobId, sessionId: 'sess-1', mode: 'compose' });
    const manifest = await store.readManifest('sess-1');
    expect(manifest?.state).toBe('queued');
    expect(manifest?.correlationId).toBe('corr-1');
    expect(manifest?.tracks.map((t) => t.trackId)).toEqual(['track-a', 'track-v']);
    expect(manifest?.tracks.every((t) => t.endedOffsetMs !== null && t.startOffsetMs >= 0)).toBe(true);
    expect(await store.readTimeline('sess-1')).toEqual(timeline);
    expect(launcher.handles.every((h) => !h.isAlive())).toBe(true);

    const late = await call('POST', '/v1/sessions/sess-1/tracks', trackBody({ trackId: 'track-late' }));
    expect(late.statusCode).toBe(409);
    expect(late.json().code).toBe('Recording.SessionClosed');
  });

  it('códigos de error: validación, sesión inexistente, video no capturado, capacidad, captura fallida', async () => {
    const { call, launcher, ports } = await setup({ maxActiveSessions: 1 });
    const bad = await call('POST', '/v1/sessions', sessionBody({ sessionId: '../etc', composition: { width: 1281 } }));
    expect(bad.statusCode).toBe(400);
    expect(bad.json().code).toBe('Recording.InvalidRequest');

    const missing = await call('POST', '/v1/sessions/none/tracks', trackBody());
    expect(missing.statusCode).toBe(404);
    expect(missing.json().code).toBe('Recording.SessionNotFound');
    expect((await call('POST', '/v1/sessions/none/stop', { stoppedAtUtc: START, reason: 'User', timeline })).json().code).toBe('Recording.SessionNotFound');

    expect((await call('POST', '/v1/sessions', sessionBody({ captureVideo: false }))).statusCode).toBe(201);
    const noVideo = await call('POST', '/v1/sessions/sess-1/tracks', trackBody({ kind: 'video', codec: { mimeType: 'video/VP8', payloadType: 101, clockRate: 90000 } }));
    expect(noVideo.statusCode).toBe(409);
    expect(noVideo.json().code).toBe('Recording.VideoNotCaptured');

    const mismatch = await call('POST', '/v1/sessions/sess-1/tracks', trackBody({ kind: 'video' }));
    expect(mismatch.statusCode).toBe(400);

    const full = await call('POST', '/v1/sessions', sessionBody({ sessionId: 'sess-2' }));
    expect(full.statusCode).toBe(503);
    expect(full.json().code).toBe('Recording.CapacityExceeded');

    launcher.failNext = new CaptureStartError('ffmpeg exited during startup: bind failed');
    const failed = await call('POST', '/v1/sessions/sess-1/tracks', trackBody());
    expect(failed.statusCode).toBe(503);
    expect(failed.json().code).toBe('Recording.CaptureFailed');
    expect(ports.inUse).toBe(0);

    const badJson = await call('POST', '/v1/sessions/sess-1/stop', undefined);
    expect(badJson.statusCode).toBe(400);
  });

  it('recompose: 409 mientras captura, 409 sin spool, 202 tras componer', async () => {
    const { call, store, queue } = await setup();
    await call('POST', '/v1/sessions', sessionBody());
    await call('POST', '/v1/sessions/sess-1/tracks', trackBody());
    const body = { timeline, composition: sessionBody().composition };
    expect((await call('POST', '/v1/sessions/sess-1/recompose', body)).json().code).toBe('Recording.SessionActive');
    expect((await call('POST', '/v1/sessions/ghost/recompose', body)).json().code).toBe('Recording.TracksPurged');

    await call('POST', '/v1/sessions/sess-1/stop', { stoppedAtUtc: new Date().toISOString(), reason: 'User', timeline });
    expect((await call('POST', '/v1/sessions/sess-1/recompose', body)).json().code).toBe('Recording.CompositionInProgress');

    const manifest = await store.readManifest('sess-1');
    if (!manifest) throw new Error('manifest');
    manifest.state = 'composed';
    await store.writeManifest(manifest);
    // Las pistas falsas no escriben archivo: sin datos en el spool → TracksPurged.
    expect((await call('POST', '/v1/sessions/sess-1/recompose', body)).json().code).toBe('Recording.TracksPurged');

    const { writeFile } = await import('node:fs/promises');
    await writeFile(store.trackPath('sess-1', 'track-a.webm'), 'data');
    const ok = await call('POST', '/v1/sessions/sess-1/recompose', { ...body, composition: { ...body.composition, format: 'webm' } });
    expect(ok.statusCode).toBe(202);
    const after = await store.readManifest('sess-1');
    expect(after?.state).toBe('queued');
    expect(after?.composition.format).toBe('webm');
    expect(after?.jobId).toBe(ok.json().jobId);
    await waitFor(async () => (await queue.depth()) === 2);
  });

  it('DELETE /v1/sessions/:id aborta, descarta el spool y es idempotente', async () => {
    const { call, store, launcher, bus } = await setup();
    await call('POST', '/v1/sessions', sessionBody());
    await call('POST', '/v1/sessions/sess-1/tracks', trackBody());
    expect((await call('DELETE', '/v1/sessions/sess-1')).statusCode).toBe(204);
    expect(launcher.handles[0]?.isAlive()).toBe(false);
    expect(await store.sessionExists('sess-1')).toBe(false);
    expect(bus.events).toHaveLength(0);
    expect((await call('DELETE', '/v1/sessions/sess-1')).statusCode).toBe(204);
    expect((await call('POST', '/v1/sessions/sess-1/tracks', trackBody())).statusCode).toBe(404);
  });

  it('DELETE de una sesión ya encolada publica composition_failed Aborted', async () => {
    const { call, bus } = await setup();
    await call('POST', '/v1/sessions', sessionBody());
    await call('POST', '/v1/sessions/sess-1/stop', { stoppedAtUtc: new Date().toISOString(), reason: 'User', timeline });
    expect((await call('DELETE', '/v1/sessions/sess-1')).statusCode).toBe(204);
    expect(bus.events.map((e) => [e.eventType, e.body['reason']])).toEqual([
      ['communication.recording.composition_failed.v1', 'Aborted'],
    ]);
  });

  it('/metrics expone las métricas del recorder', async () => {
    const { call } = await setup();
    const res = await call('GET', '/metrics', undefined, false);
    expect(res.statusCode).toBe(200);
    expect(res.body).toContain('recorder_active_sessions');
    expect(res.body).toContain('recorder_composition_failures_total');
  });
});
