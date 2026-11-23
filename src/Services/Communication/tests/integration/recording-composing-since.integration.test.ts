import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope, RecordingSessionState } from '../../src/domain/recording/recording-session-state.js';
import { PrismaRecordingSessionRepository } from '../../src/infrastructure/persistence/prisma-recording-repository.js';
import { F7_SNAPSHOT_DEFAULTS } from '../helpers/fake-recording-session-repository.js';

/** Plazo de composición contra SQL Server real: se mide desde la vuelta vigente, no desde que se detuvo. */
const prisma = new PrismaClient();
const repo = new PrismaRecordingSessionRepository(prisma);
const tenantId = randomUUID();
const HOUR_MS = 3_600_000;

function session(state: RecordingSessionState, stoppedAgoMs: number, composingAgoMs: number | null): RecordingSession {
  const now = Date.now();
  return RecordingSession.rehydrate({
    id: randomUUID(),
    tenantId,
    scope: RecordingScope.Meeting,
    scopeId: randomUUID(),
    state,
    requestedByUserId: randomUUID(),
    requestedAtUtc: new Date(now - stoppedAgoMs - HOUR_MS),
    startedAtUtc: new Date(now - stoppedAgoMs - HOUR_MS),
    stoppedAtUtc: new Date(now - stoppedAgoMs),
    recordingFileId: null,
    durationSeconds: null,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
    composingSinceUtc: composingAgoMs === null ? null : new Date(now - composingAgoMs),
  });
}

afterAll(async () => {
  await prisma.recordingSession.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('PrismaRecordingSessionRepository.listComposingSince', () => {
  it('guarda y lee el inicio de la composición vigente', async () => {
    const s = session(RecordingSessionState.Composing, 5 * HOUR_MS, 60_000);
    await repo.save(s);

    const stored = await repo.findById(tenantId, s.id);

    expect(stored?.toSnapshot().composingSinceUtc?.getTime()).toBe(s.toSnapshot().composingSinceUtc?.getTime());
  });

  it('solo devuelve composiciones cuya vuelta vigente ya pasó el plazo', async () => {
    // El caso de producción: la reunión se detuvo hace horas, pero el recompose acaba de empezar.
    const recomposedNow = session(RecordingSessionState.Composing, 5 * HOUR_MS, 60_000);
    const overdue = session(RecordingSessionState.Composing, 5 * HOUR_MS, 2 * HOUR_MS);
    // Fila anterior a la columna: se mide desde que se detuvo.
    const legacyOverdue = session(RecordingSessionState.Composing, 2 * HOUR_MS, null);
    const legacyFresh = session(RecordingSessionState.Composing, 60_000, null);
    const notComposing = session(RecordingSessionState.Failed, 5 * HOUR_MS, 2 * HOUR_MS);
    for (const s of [recomposedNow, overdue, legacyOverdue, legacyFresh, notComposing]) await repo.save(s);

    const found = (await repo.listComposingSince(new Date(Date.now() - HOUR_MS)))
      .filter((s) => s.toSnapshot().tenantId === tenantId)
      .map((s) => s.id);

    expect(found.sort()).toEqual([overdue.id, legacyOverdue.id].sort());
  });
});
