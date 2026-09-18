import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call, type CallSnapshot } from '../../src/domain/calls/call.js';
import { Conversation, type ConversationSnapshot } from '../../src/domain/conversations/conversation.js';
import type { MessageSnapshot } from '../../src/domain/conversations/message.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { PresenceService } from '../../src/application/ports/presence-service.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { CallSocketEvents } from '../../src/contracts/socket/call-socket-events.js';
import { ChatSocketEvents } from '../../src/contracts/socket/chat-socket-events.js';
import { processMissedCalls } from '../../src/application/use-cases/process-missed-calls.js';

function u(): string {
  return randomUUID();
}

class FakeCallRepository implements CallRepository {
  private readonly store = new Map<string, Call>();
  ringing: CallSnapshot[] = [];
  async save(call: Call): Promise<void> {
    this.store.set(call.id, call);
  }
  async findById(tenantId: string, callId: string): Promise<Call | null> {
    const c = this.store.get(callId);
    return c && c.tenantId === tenantId ? c : null;
  }
  async findRingingOlderThan(): Promise<CallSnapshot[]> {
    return this.ringing;
  }
  async listRecentForUser(): Promise<CallSnapshot[]> {
    return [];
  }
  async countRecentForUser(): Promise<number> {
    return 0;
  }
}

class FakeConversationRepository implements ConversationRepository {
  private readonly store = new Map<string, Conversation>();
  /** Mensajes persistidos por save() — el repo real DRENA pendingMessages y los escribe. */
  readonly persistedMessages: MessageSnapshot[] = [];
  seed(conversation: Conversation): void {
    this.store.set(conversation.id, conversation);
  }
  async save(conversation: Conversation): Promise<void> {
    for (const message of conversation.drainPendingMessages()) {
      this.persistedMessages.push(message.toSnapshot());
    }
    this.store.set(conversation.id, conversation);
  }
  async findById(tenantId: string, id: string): Promise<Conversation | null> {
    const c = this.store.get(id);
    return c && c.toSnapshot().tenantId === tenantId ? c : null;
  }
  async findByUniquenessKey(): Promise<Conversation | null> {
    return null;
  }
  async listForUser(): Promise<ConversationSnapshot[]> {
    return [];
  }
  async countForUser(): Promise<number> {
    return 0;
  }
  async listMessages(): Promise<MessageSnapshot[]> {
    return [];
  }
  async countUnreadForUser(): Promise<number> {
    return 0;
  }
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakePresence implements PresenceService {
  readonly clearedBusy: Array<{ userId: string; sourceId: string }> = [];
  async register(): Promise<void> {}
  async heartbeat(): Promise<void> {}
  async unregister(): Promise<void> {}
  async isOnline(): Promise<boolean> {
    return false;
  }
  async listOnline(): Promise<readonly string[]> {
    return [];
  }
  async markBusy(): Promise<void> {}
  async clearBusy(input: { userId: string; sourceId: string }): Promise<void> {
    this.clearedBusy.push({ userId: input.userId, sourceId: input.sourceId });
  }
}

class FakeEmitter implements RealtimeEmitter {
  readonly calls: Array<{ callId: string; event: string; payload: unknown }> = [];
  readonly conversations: Array<{ conversationId: string; event: string; payload: unknown }> = [];
  readonly users: Array<{ userId: string; event: string; payload: unknown }> = [];
  emitToConversation(input: { conversationId: string; event: string; envelope: { payload: unknown } }): void {
    this.conversations.push({ conversationId: input.conversationId, event: input.event, payload: input.envelope.payload });
  }
  emitToCall(input: { callId: string; event: string; envelope: { payload: unknown } }): void {
    this.calls.push({ callId: input.callId, event: input.event, payload: input.envelope.payload });
  }
  emitToMeeting(): void {}
  emitToUser(input: { userId: string; event: string; envelope: { payload: unknown } }): void {
    this.users.push({ userId: input.userId, event: input.event, payload: input.envelope.payload });
  }
  emitToTenant(): void {}
}

function buildHarness() {
  return {
    calls: new FakeCallRepository(),
    conversations: new FakeConversationRepository(),
    publisher: new FakePublisher(),
    presence: new FakePresence(),
    emitter: new FakeEmitter(),
  };
}

function directConversation(tenantId: string, a: { userId: string; displayName: string }, b: { userId: string; displayName: string }): Conversation {
  const result = Conversation.startDirect({
    tenantId,
    initiator: { userId: a.userId, displayName: a.displayName, actorType: 'Employee' },
    recipient: { userId: b.userId, displayName: b.displayName, actorType: 'Employee' },
  });
  if (!result.isSuccess) throw new Error(result.error.message);
  return result.value;
}

/** Una call Ringing (nunca contestada), como quedaria al llamar a un par offline. */
function ringingCall(tenantId: string, kind: 'Audio' | 'Video', conversationId: string | null) {
  const caller = { userId: u(), displayName: 'Caller' };
  const callee = { userId: u(), displayName: 'Callee' };
  const initiated = Call.initiate({ tenantId, kind, caller, callee, conversationId });
  if (!initiated.isSuccess) throw new Error(initiated.error.message);
  return { snapshot: initiated.value.toSnapshot(), caller, callee };
}

describe('processMissedCalls — notificacion realtime + registro en historial', () => {
  it('marks the call missed, emits call.state_changed=MissedCall to the call room and publishes CallMissed', async () => {
    const harness = buildHarness();
    const tenantId = u();
    const { snapshot, callee } = ringingCall(tenantId, 'Audio', null);
    harness.calls.ringing = [snapshot];

    const { processed } = await processMissedCalls({ timeoutSeconds: 60 }, harness);

    expect(processed).toBe(1);
    const stateEmit = harness.emitter.calls.find((e) => e.event === CallSocketEvents.StateChanged);
    expect(stateEmit).toBeDefined();
    expect(stateEmit?.callId).toBe(snapshot.id);
    expect((stateEmit?.payload as { status: string }).status).toBe('MissedCall');
    // El callee (online, sonando, aún NO en el room de la call) recibe el estado por su user room → cierra el modal.
    const calleeEmit = harness.emitter.users.find((e) => e.userId === callee.userId && e.event === CallSocketEvents.StateChanged);
    expect(calleeEmit).toBeDefined();
    expect((calleeEmit?.payload as { status: string }).status).toBe('MissedCall');
    expect(harness.presence.clearedBusy).toHaveLength(1);
    expect(harness.publisher.events).toHaveLength(1);
  });

  it('writes a "Missed call" System message to the conversation and emits chat.message.new', async () => {
    const harness = buildHarness();
    const tenantId = u();
    const { snapshot, caller, callee } = ringingCall(tenantId, 'Audio', u());
    const conversation = directConversation(tenantId, caller, callee);
    // La call apunta a una conversacion existente (mismo id que su conversationId).
    const withConv: CallSnapshot = { ...snapshot, conversationId: conversation.id };
    harness.conversations.seed(conversation);
    harness.calls.ringing = [withConv];

    await processMissedCalls({ timeoutSeconds: 60 }, harness);

    const msgEmit = harness.emitter.conversations.find((e) => e.event === ChatSocketEvents.MessageNew);
    expect(msgEmit).toBeDefined();
    expect(msgEmit?.conversationId).toBe(conversation.id);
    const dto = msgEmit?.payload as { kind: string; body: string };
    expect(dto.kind).toBe('System');
    expect(dto.body).toBe('Missed call');
    // Persistido por save() (historial en refresh, no solo en vivo).
    const persisted = harness.conversations.persistedMessages.some((m) => m.kind === 'System' && m.body === 'Missed call');
    expect(persisted).toBe(true);
  });

  it('uses "Missed video call" for a video call', async () => {
    const harness = buildHarness();
    const tenantId = u();
    const { snapshot, caller, callee } = ringingCall(tenantId, 'Video', u());
    const conversation = directConversation(tenantId, caller, callee);
    harness.conversations.seed(conversation);
    harness.calls.ringing = [{ ...snapshot, conversationId: conversation.id }];

    await processMissedCalls({ timeoutSeconds: 60 }, harness);

    const dto = harness.emitter.conversations.find((e) => e.event === ChatSocketEvents.MessageNew)?.payload as { body: string };
    expect(dto.body).toBe('Missed video call');
  });

  it('without a conversationId still notifies the caller but writes no chat message', async () => {
    const harness = buildHarness();
    const tenantId = u();
    const { snapshot } = ringingCall(tenantId, 'Audio', null);
    harness.calls.ringing = [snapshot];

    await processMissedCalls({ timeoutSeconds: 60 }, harness);

    expect(harness.emitter.calls.some((e) => e.event === CallSocketEvents.StateChanged)).toBe(true);
    expect(harness.emitter.conversations).toHaveLength(0);
  });
});
