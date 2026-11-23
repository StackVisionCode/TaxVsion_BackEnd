import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaLimitsRepository } from '../../src/infrastructure/persistence/prisma-settings-repository.js';
import type { TenantCommunicationLimitsSnapshot } from '../../src/domain/settings/tenant-communication-limits.js';

/**
 * Análisis 2026-11-11, con SQL real: un evento de entitlements viejo que llega tarde no pisa los límites de una
 * revisión más nueva. La misma revisión se vuelve a aplicar (idempotente). Oficina aleatoria; se borra al final.
 */
const prisma = new PrismaClient();
const repo = new PrismaLimitsRepository(prisma);
const tenantId = randomUUID();

function snapshot(revisionNumber: number | null, maxConcurrentCalls: number): TenantCommunicationLimitsSnapshot {
  return {
    tenantId,
    planCode: 'enterprise',
    maxMeetingParticipants: 10,
    maxMeetingMinutes: 60,
    maxConcurrentCalls,
    maxMonthlyMinutes: 600,
    recordingEnabled: true,
    supportEnabled: true,
    isSuspended: false,
    enabledModules: ['comms'],
    maxRecordingMinutesMeeting: 60,
    maxRecordingMinutesAudioCall: 60,
    maxRecordingMinutesVideoCall: 60,
    recordingVideoEnabled: true,
    transcriptEnabled: null,
    callsEnabled: true,
    maxCallMinutes: 60,
    revisionNumber,
    updatedAtUtc: new Date(),
  };
}

afterAll(async () => {
  await prisma.tenantCommunicationLimits.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('PrismaLimitsRepository (SQL Server) — revisión de entitlements', () => {
  it('una revisión vieja no pisa una más nueva y la misma se vuelve a aplicar', async () => {
    await repo.upsert(snapshot(5, 4));
    await repo.upsert(snapshot(3, 1));
    expect((await repo.findByTenantId(tenantId))?.maxConcurrentCalls).toBe(4);

    await repo.upsert(snapshot(5, 6));
    expect((await repo.findByTenantId(tenantId))?.maxConcurrentCalls).toBe(6);

    await repo.upsert(snapshot(8, 2));
    expect((await repo.findByTenantId(tenantId))?.maxConcurrentCalls).toBe(2);
  });

  // Plan a la medida: las claves con excepción se guardan y se leen igual; sin lista, vuelve vacía.
  it('guarda y lee las claves que son excepción de la oficina', async () => {
    await repo.upsert({ ...snapshot(20, 4), overriddenKeys: ['communication.max_meeting_minutes'] });
    expect((await repo.findByTenantId(tenantId))?.overriddenKeys).toEqual(['communication.max_meeting_minutes']);

    await repo.upsert(snapshot(21, 4));
    expect((await repo.findByTenantId(tenantId))?.overriddenKeys).toEqual([]);
  });
});
