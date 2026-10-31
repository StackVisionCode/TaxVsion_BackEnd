import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaAnalyticsRepository } from '../../src/infrastructure/persistence/prisma-analytics-repository.js';
import { PrismaLiveConcurrencyReader } from '../../src/infrastructure/persistence/prisma-live-concurrency-reader.js';

/**
 * F10 (ADD-08): snapshot diario con SQL real. Oficinas aleatorias y días de 2001 para que la purga
 * por fecha (global) no alcance datos de verdad del entorno.
 */
const prisma = new PrismaClient();
const repo = new PrismaAnalyticsRepository(prisma);
const tenantA = randomUUID();
const tenantB = randomUUID();
const DAY = '2001-01-10';

afterAll(async () => {
  await prisma.communicationAnalyticsSnapshot.deleteMany({ where: { TenantId: { in: [tenantA, tenantB] } } });
  await prisma.$disconnect();
});

describe('PrismaAnalyticsRepository (SQL Server)', () => {
  it('incrementa contadores nuevos (incluido BigInt de bytes) sobre la misma fila del día y guarda el plan', async () => {
    await repo.incrementCounters({ tenantId: tenantA, day: DAY, increments: { callSeconds: 45, recordingBytes: 5_000_000_000 }, planCode: 'starter' });
    await Promise.all([
      repo.incrementCounters({ tenantId: tenantA, day: DAY, increments: { callSeconds: 15, guestsJoined: 1 }, planCode: 'enterprise' }),
      repo.incrementCounters({ tenantId: tenantA, day: DAY, increments: { callSeconds: 10 } }),
    ]);
    const [row] = await repo.listForRange({ tenantId: tenantA, fromDay: DAY, toDay: DAY });
    expect(row?.callSeconds).toBe(70);
    expect(row?.recordingBytes).toBe(5_000_000_000);
    expect(row?.guestsJoined).toBe(1);
    expect(row?.planCode).toBe('enterprise');
  });

  it('los picos solo suben', async () => {
    await repo.raisePeaks({ tenantId: tenantA, day: DAY, peaks: { peakConcurrentMeetings: 3, peakConcurrentParticipants: 9 } });
    await repo.raisePeaks({ tenantId: tenantA, day: DAY, peaks: { peakConcurrentMeetings: 1, peakConcurrentParticipants: 12 } });
    const [row] = await repo.listForRange({ tenantId: tenantA, fromDay: DAY, toDay: DAY });
    expect([row?.peakConcurrentMeetings, row?.peakConcurrentParticipants]).toEqual([3, 12]);
  });

  it('la vista de plataforma filtra por plan y por oficina', async () => {
    await repo.incrementCounters({ tenantId: tenantB, day: DAY, increments: { messagesSent: 4 }, planCode: 'starter' });
    const starter = await repo.listAllForRange({ fromDay: DAY, toDay: DAY, planCode: 'starter' });
    expect(starter.filter((r) => r.tenantId === tenantA || r.tenantId === tenantB).map((r) => r.tenantId)).toEqual([tenantB]);
    const onlyA = await repo.listAllForRange({ fromDay: DAY, toDay: DAY, tenantId: tenantA });
    expect(onlyA.map((r) => r.tenantId)).toEqual([tenantA]);
  });

  it('la retención borra los días anteriores al corte', async () => {
    await repo.incrementCounters({ tenantId: tenantB, day: '2001-01-02', increments: { messagesSent: 1 } });
    const purged = await repo.purgeBefore('2001-01-05');
    expect(purged).toBeGreaterThanOrEqual(1);
    const left = await repo.listForRange({ tenantId: tenantB, fromDay: '2001-01-01', toDay: '2001-01-31' });
    expect(left.map((r) => r.day)).toEqual([DAY]);
  });

  it('una oficina sin reuniones en vivo cuenta cero', async () => {
    expect(await new PrismaLiveConcurrencyReader(prisma).countForTenant(tenantA)).toEqual({ meetings: 0, participants: 0 });
  });
});
