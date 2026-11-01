import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import { rejoinCall } from '../../src/application/use-cases/rejoin-call.js';

function repoWith(call: Call): CallRepository {
  return { findById: async (_t: string, id: string) => (id === call.toSnapshot().id ? call : null) } as unknown as CallRepository;
}

function acceptedCall(): Call {
  const created = Call.initiate({
    tenantId: randomUUID(),
    kind: 'Audio',
    caller: { userId: randomUUID(), displayName: 'A' },
    callee: { userId: randomUUID(), displayName: 'B' },
  });
  if (!created.isSuccess) throw new Error();
  const call = created.value;
  call.accept({ byUserId: call.toSnapshot().calleeUserId, calleeDisplayName: 'B', maxDurationMinutes: null });
  return call;
}

describe('rejoinCall', () => {
  it('un participante de una llamada abierta vuelve a la room y recibe el estado', async () => {
    const call = acceptedCall();
    const s = call.toSnapshot();
    const result = await rejoinCall({ tenantId: s.tenantId, callId: s.id, userId: s.callerUserId }, { calls: repoWith(call) });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.open).toBe(true);
    expect(result.value.state).toMatchObject({ callId: s.id, status: 'Accepted', endReason: null });
  });

  it('si ya terminó (p. ej. abandonada) no abre la room pero devuelve el fin', async () => {
    const call = acceptedCall();
    call.abandon({ lastSeenAt: new Date() });
    const s = call.toSnapshot();
    const result = await rejoinCall({ tenantId: s.tenantId, callId: s.id, userId: s.calleeUserId }, { calls: repoWith(call) });
    expect(result.isSuccess && result.value.open).toBe(false);
    expect(result.isSuccess && result.value.state.endReason).toBe('Abandoned');
  });

  it('quien no participa recibe NotFound (no revela que la llamada existe)', async () => {
    const call = acceptedCall();
    const s = call.toSnapshot();
    const result = await rejoinCall({ tenantId: s.tenantId, callId: s.id, userId: randomUUID() }, { calls: repoWith(call) });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Call.NotFound');
  });
});
