import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { PrismaClient } from '@prisma/client';
import { Call } from '../../src/domain/calls/call.js';
import { PrismaCallRepository } from '../../src/infrastructure/persistence/prisma-call-repository.js';

/** Captura lo que `save` manda a Prisma, sin base de datos. */
function capturingPrisma() {
  const callUpserts: { update: Record<string, unknown> }[] = [];
  const tx = {
    call: { upsert: async (args: { update: Record<string, unknown> }) => void callUpserts.push(args) },
    callParticipant: { upsert: async () => undefined },
  };
  const prisma = { $transaction: async (fn: (t: typeof tx) => Promise<void>) => fn(tx) } as unknown as PrismaClient;
  return { prisma, callUpserts };
}

describe('PrismaCallRepository.save', () => {
  it('al actualizar guarda el tipo: una llamada que pasó de audio a video queda en Video', async () => {
    const caller = { userId: randomUUID(), displayName: 'Caller' };
    const callee = { userId: randomUUID(), displayName: 'Callee' };
    const created = Call.initiate({ tenantId: randomUUID(), kind: 'Audio', caller, callee });
    if (!created.isSuccess) throw new Error('initiate');
    const call = created.value;
    call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Good' });
    if (!call.upgradeToVideo({ actorUserId: caller.userId }).isSuccess) throw new Error('upgrade');
    const { prisma, callUpserts } = capturingPrisma();

    await new PrismaCallRepository(prisma).save(call);

    expect(callUpserts[0]?.update).toMatchObject({ Kind: 'Video', Status: 'Active' });
  });
});
