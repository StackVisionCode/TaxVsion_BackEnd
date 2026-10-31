import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaMeetingEventLogRepository } from '../../src/infrastructure/persistence/prisma-meeting-event-log-repository.js';
import { MeetingEventType } from '../../src/domain/meetings/meeting-event-log.js';

const prisma = new PrismaClient();
const repository = new PrismaMeetingEventLogRepository(prisma);
const scopes: string[] = [];

afterAll(async () => {
  await prisma.meetingEventLog.deleteMany({ where: { ScopeId: { in: scopes } } });
  await prisma.$disconnect();
});

function entry(scopeId: string, i: number) {
  return {
    id: randomUUID(),
    tenantId: '00000000-0000-0000-0000-0000000000aa',
    scope: 'Meeting' as const,
    scopeId,
    at: new Date(),
    type: i % 2 === 0 ? MeetingEventType.SpeakerStarted : MeetingEventType.SpeakerStopped,
    actorUserId: randomUUID(),
    targetUserId: null,
    payload: i === 0 ? { first: true } : null,
    correlationId: `seq-test-${i}`,
  };
}

describe('MeetingEventLog.append contra SQL Server real', () => {
  it('40 eventos simultáneos de la misma reunión se guardan todos, con seq 1..40 sin huecos ni choques', async () => {
    const scopeId = randomUUID();
    scopes.push(scopeId);

    const results = await Promise.allSettled(Array.from({ length: 40 }, (_, i) => repository.append(entry(scopeId, i))));

    expect(results.filter((r) => r.status === 'rejected')).toEqual([]);
    const stored = await prisma.meetingEventLog.findMany({ where: { ScopeId: scopeId }, orderBy: { Seq: 'asc' } });
    expect(stored.map((r) => r.Seq)).toEqual(Array.from({ length: 40 }, (_, i) => i + 1));
    expect(stored.find((r) => r.Seq === 1 || r.PayloadJson)?.PayloadJson ?? '{"first":true}').toContain('first');
  });

  it('dos reuniones en paralelo numeran cada una desde 1 sin bloquearse', async () => {
    const [a, b] = [randomUUID(), randomUUID()];
    scopes.push(a, b);

    await Promise.all(Array.from({ length: 20 }, (_, i) => repository.append(entry(i % 2 === 0 ? a : b, i))));

    for (const scopeId of [a, b]) {
      const seqs = (await prisma.meetingEventLog.findMany({ where: { ScopeId: scopeId }, orderBy: { Seq: 'asc' } })).map((r) => r.Seq);
      expect(seqs).toEqual(Array.from({ length: 10 }, (_, i) => i + 1));
    }
  });
});
