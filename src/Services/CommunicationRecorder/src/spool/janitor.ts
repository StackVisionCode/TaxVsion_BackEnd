import type { Logger } from 'pino';
import type { SessionState } from './manifest.js';
import type { KeyedMutex, SpoolStore } from './spool-store.js';

const PURGEABLE: readonly SessionState[] = ['composed', 'failed', 'aborted'];

/**
 * Borra las carpetas de sesión cuya retención (`tracksRetentionDays` desde la última composición o el
 * fallo) venció. Las sesiones vivas o en cola nunca se tocan.
 */
export async function runJanitor(
  deps: { store: SpoolStore; locks: KeyedMutex; logger: Logger },
  now: Date,
): Promise<string[]> {
  const deleted: string[] = [];
  for (const sessionId of await deps.store.listSessionIds()) {
    try {
      await deps.locks.run(sessionId, async () => {
        const manifest = await deps.store.readManifest(sessionId);
        if (!manifest || !PURGEABLE.includes(manifest.state) || !manifest.retainUntilUtc) return;
        if (Date.parse(manifest.retainUntilUtc) > now.getTime()) return;
        await deps.store.removeSession(sessionId);
        deleted.push(sessionId);
      });
    } catch (err) {
      deps.logger.warn({ sessionId, err: (err as Error).message }, 'janitor: could not inspect session');
    }
  }
  if (deleted.length > 0) deps.logger.info({ deleted: deleted.length }, 'janitor: expired session spools removed');
  return deleted;
}

export function retainUntil(now: Date, days: number): string {
  return new Date(now.getTime() + days * 86_400_000).toISOString();
}
