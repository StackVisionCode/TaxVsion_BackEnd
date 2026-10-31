import { mkdtemp } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { SignJWT, createLocalJWKSet, exportJWK, generateKeyPair, type JWTVerifyGetKey } from 'jose';
import { pino } from 'pino';
import type { CaptureHandle, CaptureLauncher, CaptureSpec } from '../../src/capture/ffmpeg-capture.js';
import type { QueueRedis } from '../../src/queue/compose-queue.js';
import type { SessionManifest } from '../../src/spool/manifest.js';
import type { EventBus } from '../../src/rabbit/event-bus.js';
import { createMetrics } from '../../src/telemetry/metrics.js';

export const silentLogger = pino({ level: 'silent' });
export const testMetrics = () => createMetrics(false);

export async function tempDir(prefix = 'recorder-test-'): Promise<string> {
  return mkdtemp(path.join(os.tmpdir(), prefix));
}

/** Doble en memoria de las listas de Redis que usa la cola. */
export class InMemoryRedis implements QueueRedis {
  readonly lists = new Map<string, string[]>();

  private list(key: string): string[] {
    let l = this.lists.get(key);
    if (!l) {
      l = [];
      this.lists.set(key, l);
    }
    return l;
  }

  async lpush(key: string, ...values: string[]): Promise<number> {
    const l = this.list(key);
    for (const v of values) l.unshift(v);
    return l.length;
  }

  async lmove(source: string, destination: string, from: 'LEFT' | 'RIGHT', to: 'LEFT' | 'RIGHT'): Promise<string | null> {
    const src = this.list(source);
    const value = from === 'LEFT' ? src.shift() : src.pop();
    if (value === undefined) return null;
    const dst = this.list(destination);
    if (to === 'LEFT') dst.unshift(value);
    else dst.push(value);
    return value;
  }

  async blmove(source: string, destination: string, from: 'LEFT' | 'RIGHT', to: 'LEFT' | 'RIGHT'): Promise<string | null> {
    return this.lmove(source, destination, from, to);
  }

  async lrem(key: string, count: number, value: string): Promise<number> {
    const l = this.list(key);
    let removed = 0;
    for (let i = 0; i < l.length && (count === 0 || removed < count); ) {
      if (l[i] === value) {
        l.splice(i, 1);
        removed += 1;
      } else i += 1;
    }
    return removed;
  }

  async lrange(key: string, start: number, stop: number): Promise<string[]> {
    const l = this.list(key);
    return l.slice(start, stop === -1 ? undefined : stop + 1);
  }

  async llen(key: string): Promise<number> {
    return this.list(key).length;
  }
}

export interface PublishedEvent {
  readonly eventType: string;
  readonly eventId: string;
  readonly body: Record<string, unknown>;
}

export class FakeBus implements EventBus {
  readonly events: PublishedEvent[] = [];
  readonly queued: { queue: string; messageId: string; body: Record<string, unknown> }[] = [];
  failEvents = 0;

  async publishEvent(eventType: string, eventId: string, body: object): Promise<void> {
    if (this.failEvents > 0) {
      this.failEvents -= 1;
      throw new Error('broker down');
    }
    this.events.push({ eventType, eventId, body: body as Record<string, unknown> });
  }

  async sendToQueue(queue: string, messageId: string, body: object): Promise<void> {
    this.queued.push({ queue, messageId, body: body as Record<string, unknown> });
  }
}

export class FakeCaptureHandle implements CaptureHandle {
  readonly exited: Promise<number | null>;
  stopCalls = 0;
  private alive = true;
  private resolveExit!: (code: number | null) => void;

  constructor(readonly spec: CaptureSpec) {
    this.exited = new Promise((resolve) => (this.resolveExit = resolve));
  }

  isAlive(): boolean {
    return this.alive;
  }

  async stop(): Promise<void> {
    this.stopCalls += 1;
    this.exit(0);
  }

  exit(code: number | null): void {
    if (!this.alive) return;
    this.alive = false;
    this.resolveExit(code);
  }
}

export class FakeLauncher implements CaptureLauncher {
  readonly handles: FakeCaptureHandle[] = [];
  failNext: Error | null = null;

  async launch(spec: CaptureSpec): Promise<CaptureHandle> {
    if (this.failNext) {
      const err = this.failNext;
      this.failNext = null;
      throw err;
    }
    const handle = new FakeCaptureHandle(spec);
    this.handles.push(handle);
    return handle;
  }
}

export interface TestKeys {
  readonly jwks: JWTVerifyGetKey;
  sign(claims: Record<string, unknown>, opts?: { issuer?: string; audience?: string; expired?: boolean }): Promise<string>;
}

export const ISSUER = 'TaxVision.Auth';
export const AUDIENCE = 'TaxVision.Services';

export async function testKeys(): Promise<TestKeys> {
  const { publicKey, privateKey } = await generateKeyPair('RS256');
  const jwk = { ...(await exportJWK(publicKey)), kid: 'test', alg: 'RS256', use: 'sig' };
  const jwks = createLocalJWKSet({ keys: [jwk] });
  return {
    jwks,
    async sign(claims, opts = {}) {
      const now = Math.floor(Date.now() / 1000);
      return new SignJWT(claims)
        .setProtectedHeader({ alg: 'RS256', kid: 'test' })
        .setIssuer(opts.issuer ?? ISSUER)
        .setAudience(opts.audience ?? AUDIENCE)
        .setIssuedAt(now - 60)
        .setExpirationTime(opts.expired ? now - 30 : now + 300)
        .sign(privateKey);
    },
  };
}

export const serviceClaims = (clientId = 'communication-worker') => ({
  sub: 'svc',
  tenant_id: '00000000-0000-0000-0000-000000000001',
  actor_type: 'Service',
  client_id: clientId,
});

export const flush = () => new Promise((resolve) => setImmediate(resolve));

export async function waitFor(predicate: () => boolean | Promise<boolean>, timeoutMs = 2000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!(await predicate())) {
    if (Date.now() > deadline) throw new Error('waitFor timed out');
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

export function makeManifest(sessionId: string, over: Partial<SessionManifest> = {}): SessionManifest {
  return {
    version: 1,
    sessionId,
    tenantId: 'tenant',
    scope: 'Meeting',
    scopeId: 'meeting',
    startedAtUtc: '2026-10-30T15:00:00.000Z',
    maxDurationSeconds: 3600,
    toleranceSeconds: 120,
    tracksRetentionDays: 7,
    captureVideo: true,
    composition: { width: 1280, height: 720, fps: 25, videoKbps: 1500, audioKbps: 128, format: 'mp4' },
    correlationId: 'corr',
    createdAtUtc: '2026-10-30T15:00:00.000Z',
    state: 'capturing',
    tracks: [],
    updatedAtUtc: '2026-10-30T15:00:00.000Z',
    ...over,
  };
}

