import { describe, expect, it, vi } from 'vitest';
import type { PrismaClient } from '@prisma/client';
import { purgeCommunicationHistory } from '../../src/infrastructure/schedulers/history-purge.js';

const NOW = new Date('2026-10-31T00:00:00Z');

function fakePrisma(options: { meetings?: string[]; liveSessionIds?: string[]; liveTranscriptIds?: string[]; invitations?: string[] } = {}) {
  const deleteMany = () => vi.fn().mockResolvedValue({ count: 0 });
  const prisma = {
    call: { findMany: vi.fn().mockResolvedValue([]), deleteMany: deleteMany() },
    meeting: { findMany: vi.fn().mockResolvedValue((options.meetings ?? []).map((Id) => ({ Id }))), deleteMany: deleteMany() },
    recordingSession: {
      findMany: vi.fn().mockResolvedValue((options.liveSessionIds ?? []).map((ScopeId) => ({ ScopeId }))),
      deleteMany: deleteMany(),
    },
    transcriptArtifact: {
      findMany: vi.fn().mockResolvedValue((options.liveTranscriptIds ?? []).map((ScopeId) => ({ ScopeId }))),
      deleteMany: deleteMany(),
    },
    recordingConsentEvent: { deleteMany: deleteMany() },
    meetingEventLog: { deleteMany: deleteMany() },
    meetingInvitation: {
      findMany: vi.fn().mockResolvedValue((options.invitations ?? []).map((Id) => ({ Id }))),
      deleteMany: vi.fn().mockResolvedValue({ count: (options.invitations ?? []).length }),
    },
    $transaction: vi.fn().mockResolvedValue([]),
  };
  return prisma;
}

/** F10 (ADD-08 T-73): histórico con retención de plataforma; nunca deja archivos huérfanos. */
describe('purgeCommunicationHistory', () => {
  it('con las retenciones en null no consulta ni borra nada', async () => {
    const prisma = fakePrisma({ meetings: ['m1'] });
    const result = await purgeCommunicationHistory(prisma as unknown as PrismaClient, {
      now: NOW,
      retention: { callsRetentionDays: null, meetingsRetentionDays: null, invitationsRetentionDays: null },
      batchSize: 50,
    });
    expect(result).toEqual({ calls: 0, meetings: 0, invitations: 0 });
    expect(prisma.meeting.findMany).not.toHaveBeenCalled();
    expect(prisma.$transaction).not.toHaveBeenCalled();
  });

  it('solo busca reuniones terminadas, sin archivos y más viejas que el corte', async () => {
    const prisma = fakePrisma();
    await purgeCommunicationHistory(prisma as unknown as PrismaClient, {
      now: NOW,
      retention: { callsRetentionDays: null, meetingsRetentionDays: 365, invitationsRetentionDays: null },
      batchSize: 50,
    });
    const where = prisma.meeting.findMany.mock.calls[0]?.[0].where;
    expect(where.Status).toEqual({ in: ['Ended', 'Cancelled', 'Expired'] });
    expect(where.RecordingFileId).toBeNull();
    expect(where.TranscriptFileId).toBeNull();
    expect(where.OR[0]).toEqual({ EndedAtUtc: { lt: new Date('2025-10-31T00:00:00Z') } });
  });

  it('salta las reuniones con una grabación o un transcript que aún tiene archivos', async () => {
    const prisma = fakePrisma({ meetings: ['m1', 'm2', 'm3'], liveSessionIds: ['m2'], liveTranscriptIds: ['m3'] });
    const result = await purgeCommunicationHistory(prisma as unknown as PrismaClient, {
      now: NOW,
      retention: { callsRetentionDays: null, meetingsRetentionDays: 365, invitationsRetentionDays: null },
      batchSize: 50,
    });
    expect(result.meetings).toBe(1);
    expect(prisma.meeting.deleteMany).toHaveBeenCalledWith({ where: { Id: { in: ['m1'] } } });
    expect(prisma.meetingEventLog.deleteMany).toHaveBeenCalledWith({ where: { ScopeId: { in: ['m1'] } } });
    expect(prisma.$transaction).toHaveBeenCalledTimes(1);
  });

  it('borra invitaciones cerradas antes del corte', async () => {
    const prisma = fakePrisma({ invitations: ['i1', 'i2'] });
    const result = await purgeCommunicationHistory(prisma as unknown as PrismaClient, {
      now: NOW,
      retention: { callsRetentionDays: null, meetingsRetentionDays: null, invitationsRetentionDays: 90 },
      batchSize: 50,
    });
    expect(result.invitations).toBe(2);
    expect(prisma.meetingInvitation.deleteMany).toHaveBeenCalledWith({ where: { Id: { in: ['i1', 'i2'] } } });
  });
});
