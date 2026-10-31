import { mkdir, readdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import type { Timeline } from '../contracts/api.js';
import type { SessionManifest } from './manifest.js';

/**
 * Spool en disco: `{root}/{sessionId}/` con `manifest.json`, `timeline.json`, `{trackId}.webm|.sdp|.log`.
 * Las escrituras son atómicas (tmp + rename) para que una caída no deje un manifest a medias.
 */
export class SpoolStore {
  constructor(public readonly root: string) {}

  async init(): Promise<void> {
    await mkdir(this.root, { recursive: true });
  }

  sessionDir(sessionId: string): string {
    return path.join(this.root, sessionId);
  }

  trackPath(sessionId: string, fileName: string): string {
    return path.join(this.sessionDir(sessionId), fileName);
  }

  private manifestPath(sessionId: string): string {
    return path.join(this.sessionDir(sessionId), 'manifest.json');
  }

  private timelinePath(sessionId: string): string {
    return path.join(this.sessionDir(sessionId), 'timeline.json');
  }

  async createSessionDir(sessionId: string): Promise<void> {
    await mkdir(this.sessionDir(sessionId), { recursive: true });
  }

  async readManifest(sessionId: string): Promise<SessionManifest | null> {
    try {
      const raw = await readFile(this.manifestPath(sessionId), 'utf-8');
      return JSON.parse(raw) as SessionManifest;
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null;
      throw err;
    }
  }

  async writeManifest(manifest: SessionManifest): Promise<void> {
    await this.atomicWrite(this.manifestPath(manifest.sessionId), JSON.stringify(manifest, null, 2));
  }

  async readTimeline(sessionId: string): Promise<Timeline | null> {
    try {
      return JSON.parse(await readFile(this.timelinePath(sessionId), 'utf-8')) as Timeline;
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null;
      throw err;
    }
  }

  async writeTimeline(sessionId: string, timeline: Timeline): Promise<void> {
    await this.atomicWrite(this.timelinePath(sessionId), JSON.stringify(timeline));
  }

  async listSessionIds(): Promise<string[]> {
    try {
      const entries = await readdir(this.root, { withFileTypes: true });
      return entries.filter((e) => e.isDirectory()).map((e) => e.name);
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === 'ENOENT') return [];
      throw err;
    }
  }

  async sessionExists(sessionId: string): Promise<boolean> {
    try {
      return (await stat(this.sessionDir(sessionId))).isDirectory();
    } catch {
      return false;
    }
  }

  /** Tamaño del archivo o 0 si no existe. */
  async fileSize(filePath: string): Promise<number> {
    try {
      return (await stat(filePath)).size;
    } catch {
      return 0;
    }
  }

  async removeSession(sessionId: string): Promise<void> {
    await rm(this.sessionDir(sessionId), { recursive: true, force: true });
  }

  private async atomicWrite(target: string, content: string): Promise<void> {
    const tmp = `${target}.${process.pid}.${Date.now()}.tmp`;
    await writeFile(tmp, content, 'utf-8');
    await rename(tmp, target);
  }
}

/** Serializa operaciones por clave (una sesión) dentro del proceso. */
export class KeyedMutex {
  private readonly tails = new Map<string, Promise<unknown>>();

  async run<T>(key: string, fn: () => Promise<T>): Promise<T> {
    const prev = this.tails.get(key) ?? Promise.resolve();
    const next = prev.then(fn, fn);
    const tail = next.catch(() => undefined);
    this.tails.set(key, tail);
    try {
      return await next;
    } finally {
      if (this.tails.get(key) === tail) this.tails.delete(key);
    }
  }
}
