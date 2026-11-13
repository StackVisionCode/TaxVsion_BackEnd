import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { IdempotencyStore } from '../../src/application/ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { stopCallScreenShare } from '../../src/application/use-cases/stop-call-screen-share.js';

function sharingCall() {
  const caller = { userId: randomUUID(), displayName: 'Caller' };
  const callee = { userId: randomUUID(), displayName: 'Callee' };
  const call = Call.initiate({ tenantId: randomUUID(), kind: 'Video', caller, callee });
  if (!call.isSuccess) throw new Error('initiate');
  if (!call.value.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName }).isSuccess) throw new Error('accept');
  if (!call.value.markActive().isSuccess) throw new Error('active');
  if (!call.value.startScreenShare({ actorUserId: caller.userId }).isSuccess) throw new Error('share');
  return { call: call.value, caller };
}

function harness(call: Call) {
  const closed: { meetingId: string; userId: string; source: string }[] = [];
  const deps = {
    calls: {
      findById: async (tenantId: string, id: string) => (id === call.id && tenantId === call.tenantId ? call : null),
      save: async () => undefined,
    } as unknown as CallRepository,
    idempotency: {
      tryReserve: async () => ({ status: 'reserved', token: 't' }),
      commit: async () => undefined,
      release: async () => undefined,
    } as unknown as IdempotencyStore,
    publisher: { enqueue: async () => undefined } as IntegrationEventPublisher,
    emitter: { emitToCall: () => undefined } as unknown as RealtimeEmitter,
    sfu: {
      closeProducersBySource: (input: { meetingId: string; userId: string; source: 'camera' | 'screen' }) => {
        closed.push(input);
        return 1;
      },
    },
  };
  return { deps, closed };
}

describe('stopCallScreenShare · captura de la grabación', () => {
  it('al dejar de compartir cierra la pantalla de la captura: el recorder corta ese tramo', async () => {
    const { call, caller } = sharingCall();
    const { deps, closed } = harness(call);

    const result = await stopCallScreenShare(
      { tenantId: call.tenantId, correlationId: 'c', clientKey: 'k', callId: call.id, actorUserId: caller.userId },
      deps,
    );

    expect(result.isSuccess).toBe(true);
    expect(closed).toEqual([{ meetingId: call.id, userId: caller.userId, source: 'screen' }]);
  });

  it('aunque el dominio rechace el stop (llamada sin Active), la pantalla de la captura se cierra', async () => {
    const caller = { userId: randomUUID(), displayName: 'Caller' };
    const callee = { userId: randomUUID(), displayName: 'Callee' };
    const initiated = Call.initiate({ tenantId: randomUUID(), kind: 'Video', caller, callee });
    if (!initiated.isSuccess) throw new Error('initiate');
    const call = initiated.value;
    call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
    const { deps, closed } = harness(call);

    const result = await stopCallScreenShare(
      { tenantId: call.tenantId, correlationId: 'c', clientKey: 'k', callId: call.id, actorUserId: caller.userId },
      deps,
    );

    expect(result.isSuccess).toBe(false);
    expect(closed).toEqual([{ meetingId: call.id, userId: caller.userId, source: 'screen' }]);
  });

  it('quien no participa en la llamada no cierra nada', async () => {
    const { call } = sharingCall();
    const { deps, closed } = harness(call);

    await stopCallScreenShare(
      { tenantId: call.tenantId, correlationId: 'c', clientKey: 'k', callId: call.id, actorUserId: randomUUID() },
      deps,
    );

    expect(closed).toEqual([]);
  });

  it('si el SFU falla, la llamada deja de compartir igual', async () => {
    const { call, caller } = sharingCall();
    const { deps } = harness(call);
    deps.sfu.closeProducersBySource = () => {
      throw new Error('sfu down');
    };

    const result = await stopCallScreenShare(
      { tenantId: call.tenantId, correlationId: 'c', clientKey: 'k', callId: call.id, actorUserId: caller.userId },
      deps,
    );

    expect(result.isSuccess).toBe(true);
  });
});
