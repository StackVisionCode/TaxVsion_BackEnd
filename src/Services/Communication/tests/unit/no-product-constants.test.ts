import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

/**
 * Fitness (principio del dueño: nada hard-coded): los valores de producto de la lista C.1 viven en
 * PlatformCommunicationSettings y se leen por el resolver; en código solo quedan los techos
 * técnicos de domain/settings/technical-limits.ts. Si una de estas constantes reaparece, el test
 * falla antes de que vuelva a decidir comportamiento.
 */
const RETIRED_IDENTIFIERS = [
  'DISCONNECT_GRACE_MS',
  'INVITATION_TTL_SECONDS',
  'OUTBOX_RETENTION_DAYS',
  'PROCESSED_EVENT_RETENTION_DAYS',
  'IDEMPOTENCY_RETENTION_DAYS',
  'MAX_PER_SECOND',
  'COMMUNICATION_MEETING_MESH_MAX_PARTICIPANTS',
  'COMMUNICATION_MEETING_DEFAULT_MAX_PARTICIPANTS',
  'ringingTimeoutSeconds: 60',
  'meetingTimeoutSeconds: 30',
  'callTimeoutSeconds: 15',
];

const SRC_ROOT = join(process.cwd(), 'src');

function listTsFiles(dir: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) files.push(...listTsFiles(full));
    else if (full.endsWith('.ts')) files.push(full);
  }
  return files;
}

describe('product constants live in the configuration layer, not in code', () => {
  it('no retired constant from Revision C.1 is referenced anywhere under src/', () => {
    const offenders: string[] = [];
    for (const file of listTsFiles(SRC_ROOT)) {
      const text = readFileSync(file, 'utf8');
      for (const identifier of RETIRED_IDENTIFIERS) {
        // Palabra completa: PRESENCE_DISCONNECT_GRACE_MS (2 s, técnico) no es DISCONNECT_GRACE_MS.
        if (new RegExp(`(?<![A-Za-z0-9_])${identifier}(?![A-Za-z0-9_])`).test(text)) {
          offenders.push(`${relative(SRC_ROOT, file)} -> ${identifier}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });

  it('every reader of a product value goes through the resolver or the platform snapshot', () => {
    const consumers = [
      'api/socket/handlers/meeting-handlers.ts',
      'application/use-cases/create-meeting-invitations.ts',
      'application/use-cases/start-group-conversation.ts',
      'infrastructure/schedulers/missed-call-scheduler.ts',
      'infrastructure/schedulers/recording-consent-timeout-scheduler.ts',
      'infrastructure/schedulers/purge-scheduler.ts',
    ];
    for (const consumer of consumers) {
      const text = readFileSync(join(SRC_ROOT, consumer), 'utf8');
      expect(text, consumer).toMatch(/effectiveSettings/);
    }
  });
});
