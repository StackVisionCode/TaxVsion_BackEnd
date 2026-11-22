import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { CallMissedEvent } from '../../src/contracts/events/call-events.js';
import { respondToCall } from '../../src/application/use-cases/respond-to-call.js';

const u = () => randomUUID();

function setup() {
  const tenantId = u();
  const caller = { userId: u(), displayName: 'Caller' };
  const callee = { userId: u(), displayName: 'Callee' };
  const started = Conversation.startDirect({
    tenantId,
    initiator: { ...caller, actorType: 'Employee' },
    recipient: { ...callee, actorType: 'Employee' },
  });
  if (!started.isSuccess) throw new Error(started.error.message);
  const conversation = started.value;
  const initiated = Call.initiate({ tenantId, kind: 'Audio', caller, callee, conversationId: conversation.id });
  if (!initiated.isSuccess) throw new Error(initiated.error.message);
  const call = initiated.value;
  const systemBodies: string[] = [];
  const published: IntegrationEvent[] = [];
  const deps = {
    calls: {
      findById: async (t: string, id: string) => (t === tenantId && id === call.id ? call : null),
      save: async () => undefined,
    } as unknown as CallRepository,
    conversations: {
      findById: async (t: string, id: string) => (t === tenantId && id === conversation.id ? conversation : null),
      save: async (c: Conversation) => {
        for (const m of c.drainPendingMessages()) systemBodies.push(m.toSnapshot().body ?? '');
      },
    } as unknown as ConversationRepository,
    emitter: {
      emitToConversation: () => undefined,
      emitToUser: () => undefined,
      emitToCall: () => undefined,
    } as unknown as RealtimeEmitter,
    idempotency: {
      tryReserve: async () => ({ status: 'fresh' as const, token: u() }),
      commit: async () => undefined,
      release: async () => undefined,
    },
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
  };
  const respond = (action: 'accept' | 'reject' | 'cancel', actorUserId: string) =>
    respondToCall(
      { tenantId, correlationId: 'c', clientKey: u(), callId: call.id, actorUserId, actorDisplayName: 'x', action },
      deps,
    );
  return { call, conversation, caller, callee, respond, published, systemBodies };
}

describe('respondToCall — llamada sin contestar', () => {
  it('cancel before answer publishes a missed call for the callee', async () => {
    const { respond, caller, callee, published, systemBodies, conversation, call } = setup();

    expect((await respond('cancel', caller.userId)).isSuccess).toBe(true);

    expect(published.map((e) => e.eventType)).toContain('communication.call.ended.v1');
    const missed = published.find((e): e is CallMissedEvent => e.eventType === 'communication.call.missed.v1');
    expect(missed).toMatchObject({
      callId: call.id,
      calleeUserId: callee.userId,
      reason: 'Cancelled',
      conversationId: conversation.id,
    });
    expect(systemBodies).toEqual(['Missed call']);
  });

  it('reject appends a declined-call message and publishes no missed call', async () => {
    const { respond, callee, published, systemBodies } = setup();

    expect((await respond('reject', callee.userId)).isSuccess).toBe(true);

    expect(published.map((e) => e.eventType)).not.toContain('communication.call.missed.v1');
    expect(systemBodies).toEqual(['Declined call']);
  });

  it('accept publishes nothing extra', async () => {
    const { respond, callee, published, systemBodies } = setup();

    expect((await respond('accept', callee.userId)).isSuccess).toBe(true);

    expect(published.map((e) => e.eventType)).not.toContain('communication.call.missed.v1');
    expect(systemBodies).toEqual([]);
  });
});
