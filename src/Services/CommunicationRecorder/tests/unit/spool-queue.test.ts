import { readdir, rm, writeFile } from 'node:fs/promises';
import { afterEach, describe, expect, it } from 'vitest';
import { ComposeQueue } from '../../src/queue/compose-queue.js';
import { retainUntil, runJanitor } from '../../src/spool/janitor.js';
import type { SessionManifest } from '../../src/spool/manifest.js';
import { KeyedMutex, SpoolStore } from '../../src/spool/spool-store.js';
import { InMemoryRedis, makeManifest as manifest, silentLogger, tempDir } from './helpers.js';

const dirs: string[] = [];
afterEach(async () => {
  while (dirs.length > 0) await rm(dirs.pop() as string, { recursive: true, force: true });
});

async function newStore(): Promise<SpoolStore> {
  const dir = await tempDir();
  dirs.push(dir);
  const store = new SpoolStore(dir);
  await store.init();
  return store;
}

describe('SpoolStore', () => {
  it('manifest y timeline con escritura atómica (sin .tmp residual)', async () => {
    const store = await newStore();
    await store.createSessionDir('s1');
    expect(await store.readManifest('s1')).toBeNull();
    await store.writeManifest(manifest('s1'));
    await store.writeManifest(manifest('s1', { state: 'queued' }));
    expect((await store.readManifest('s1'))?.state).toBe('queued');
    await store.writeTimeline('s1', { participants: [{ userId: 'u' }], events: [] });
    expect(await store.readTimeline('s1')).toEqual({ participants: [{ userId: 'u' }], events: [] });
    expect((await readdir(store.sessionDir('s1'))).sort()).toEqual(['manifest.json', 'timeline.json']);
    expect(await store.listSessionIds()).toEqual(['s1']);
    await writeFile(store.trackPath('s1', 't.webm'), 'abc');
    expect(await store.fileSize(store.trackPath('s1', 't.webm'))).toBe(3);
    expect(await store.fileSize(store.trackPath('s1', 'missing.webm'))).toBe(0);
  });

  it('KeyedMutex serializa por clave', async () => {
    const locks = new KeyedMutex();
    const order: string[] = [];
    const slow = locks.run('k', async () => {
      await new Promise((r) => setTimeout(r, 20));
      order.push('a');
    });
    const fast = locks.run('k', async () => {
      order.push('b');
    });
    const other = locks.run('z', async () => {
      order.push('z');
    });
    await Promise.all([slow, fast, other]);
    expect(order).toEqual(['z', 'a', 'b']);
    await expect(locks.run('k', async () => Promise.reject(new Error('x')))).rejects.toThrow('x');
    expect(await locks.run('k', async () => 1)).toBe(1);
  });
});

describe('janitor', () => {
  it('borra solo sesiones terminadas con retención vencida', async () => {
    const store = await newStore();
    const now = new Date('2026-11-10T00:00:00.000Z');
    const cases: [string, Partial<SessionManifest>][] = [
      ['expired-composed', { state: 'composed', retainUntilUtc: '2026-11-09T00:00:00.000Z' }],
      ['expired-failed', { state: 'failed', retainUntilUtc: '2026-11-01T00:00:00.000Z' }],
      ['aborted', { state: 'aborted', retainUntilUtc: '2026-11-09T23:59:59.000Z' }],
      ['fresh', { state: 'composed', retainUntilUtc: '2026-11-12T00:00:00.000Z' }],
      ['capturing', { state: 'capturing', retainUntilUtc: '2026-01-01T00:00:00.000Z' }],
      ['queued', { state: 'queued' }],
    ];
    for (const [id, over] of cases) {
      await store.createSessionDir(id);
      await store.writeManifest(manifest(id, over));
    }
    await store.createSessionDir('no-manifest');
    const deleted = await runJanitor({ store, locks: new KeyedMutex(), logger: silentLogger }, now);
    expect(deleted.sort()).toEqual(['aborted', 'expired-composed', 'expired-failed']);
    expect((await store.listSessionIds()).sort()).toEqual(['capturing', 'fresh', 'no-manifest', 'queued']);
    expect(retainUntil(new Date('2026-11-01T00:00:00.000Z'), 7)).toBe('2026-11-08T00:00:00.000Z');
  });
});

describe('ComposeQueue (Redis en memoria)', () => {
  const job = (jobId: string) => ({ jobId, sessionId: `s-${jobId}`, mode: 'compose' as const, enqueuedAtUtc: '2026-10-30T15:00:00.000Z' });

  it('FIFO, take mueve a en-curso, ack lo quita', async () => {
    const redis = new InMemoryRedis();
    const q = new ComposeQueue(redis, redis, 'recorder');
    await q.enqueue(job('1'));
    await q.enqueue(job('2'));
    expect(await q.depth()).toBe(2);
    const first = await q.take(1);
    expect(first?.job.jobId).toBe('1');
    expect(await redis.llen('recorder:compose:processing')).toBe(1);
    expect(await q.contains('1')).toBe(true);
    await q.ack(first?.raw ?? '');
    expect(await q.contains('1')).toBe(false);
    expect((await q.take(1))?.job.jobId).toBe('2');
    expect(await q.take(1)).toBeNull();
  });

  it('recover() devuelve lo que quedó en curso al frente de la cola', async () => {
    const redis = new InMemoryRedis();
    const q = new ComposeQueue(redis, redis, 'recorder');
    await q.enqueue(job('a'));
    await q.enqueue(job('b'));
    await q.take(1); // 'a' queda en curso (caída simulada)
    expect(await q.recover()).toBe(1);
    expect(await redis.llen('recorder:compose:processing')).toBe(0);
    expect((await q.take(1))?.job.jobId).toBe('a');
    expect((await q.take(1))?.job.jobId).toBe('b');
  });

  it('requeue manda el trabajo al final; un valor ilegible se descarta', async () => {
    const redis = new InMemoryRedis();
    const q = new ComposeQueue(redis, redis, 'recorder');
    await q.enqueue(job('x'));
    await q.enqueue(job('y'));
    const x = await q.take(1);
    await q.requeue(x?.raw ?? '');
    expect((await q.take(1))?.job.jobId).toBe('y');
    expect((await q.take(1))?.job.jobId).toBe('x');
    await redis.lpush('recorder:compose:queue', '{not json');
    expect(await q.take(1)).toBeNull();
    expect(await redis.lrange('recorder:compose:processing', 0, -1)).not.toContain('{not json'); // el ilegible ya no está
  });
});
