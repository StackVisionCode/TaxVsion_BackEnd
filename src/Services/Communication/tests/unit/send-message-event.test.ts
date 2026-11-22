import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { sendMessage } from '../../src/application/use-cases/send-message.js';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationKind } from '../../src/domain/conversations/conversation-kind.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { MessageSentEvent } from '../../src/contracts/events/chat-events.js';
import { conversationWith } from '../helpers/chat-file-library.js';
import type { ConversationFocusStore } from '../../src/application/ports/conversation-focus-store.js';


const u = () => randomUUID();

interface FocusOptions {
  readonly conversationFocus?: ConversationFocusStore;
  readonly chatPushSuppressWhenActive?: boolean;
}

function setup(
  kind: ConversationKind,
  extra: { meetingId?: string; ticketId?: string; others?: number } = {},
  focusOptions: FocusOptions = {},
) {
  const tenantId = u();
  const senderId = u();
  const others = Array.from({ length: extra.others ?? 2 }, () => ({ userId: u() }));
  const conversation = conversationWith({
    tenantId,
    kind,
    participants: [{ userId: senderId }, ...others],
    ...(extra.meetingId ? { meetingId: extra.meetingId } : {}),
    ...(extra.ticketId ? { ticketId: extra.ticketId } : {}),
  });
  const published: IntegrationEvent[] = [];
  const deps = {
    conversations: {
      findById: async (t: string, id: string) => (t === tenantId && id === conversation.id ? conversation : null),
      save: async (_c: Conversation) => undefined,
    } as unknown as ConversationRepository,
    idempotency: {
      tryReserve: async () => ({ status: 'fresh' as const, token: u() }),
      commit: async () => undefined,
      release: async () => undefined,
    },
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
    settings: {
      get: async (t: string) => ({
        tenantId: t,
        chatEnabled: true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: false,
        screenshotsEnabled: true,
      }),
    },
    attachmentTracking: { register: async () => undefined, markStatus: async () => null, findByFileId: async () => null },
    attachmentReservations: { reserve: async () => undefined, consume: async () => true, purgeOlderThan: async () => 0 },
    ...focusOptions,
  };
  const send = async () => {
    const result = await sendMessage(
      { tenantId, correlationId: 'corr', clientKey: u(), conversationId: conversation.id, senderUserId: senderId, body: 'hi' },
      deps,
    );
    expect(result.isSuccess).toBe(true);
    return published.find((e): e is MessageSentEvent => e.eventType === 'communication.chat.message_sent.v1')!;
  };
  return { conversation, others: others.map((o) => o.userId), send };
}

describe('sendMessage — evento de mensaje enviado', () => {
  it('publishes the conversation kind and the meeting of a meeting chat', async () => {
    const meetingId = u();
    const { send } = setup('Meeting', { meetingId });

    const event = await send();

    expect(event.conversationKind).toBe('Meeting');
    expect(event.meetingId).toBe(meetingId);
  });

  it('a meeting chat leaves nobody to push', async () => {
    const { send } = setup('Meeting', { meetingId: u() });

    const event = await send();

    expect(event.pushRecipientUserIds).toEqual([]);
  });

  it('a support chat carries its ticket', async () => {
    const ticketId = u();
    const { send } = setup('Support', { ticketId, others: 1 });

    const event = await send();

    expect(event.conversationKind).toBe('Support');
    expect(event.supportTicketId).toBe(ticketId);
  });

  it('who muted the conversation stays a recipient but is not pushed', async () => {
    const { conversation, others, send } = setup('Group');
    const [muted, free] = others as [string, string];
    expect(conversation.setMuted(muted, true).isSuccess).toBe(true);

    const event = await send();

    expect(event.recipientUserIds).toContain(muted);
    expect(event.pushRecipientUserIds).toEqual([free]);
  });

  it('who is viewing the conversation or has the app visible is not pushed', async () => {
    const holder: { focus: { active: string[]; viewing: string[] } } = { focus: { active: [], viewing: [] } };
    const store: ConversationFocusStore = {
      set: async () => undefined,
      clear: async () => undefined,
      snapshot: async () => holder.focus,
    };
    const { others, send } = setup('Group', { others: 3 }, { conversationFocus: store });
    const [viewing, active, away] = others as [string, string, string];
    holder.focus = { active: [viewing, active], viewing: [viewing] };

    const event = await send();

    expect(event.recipientUserIds).toEqual([viewing, active, away]);
    expect(event.pushRecipientUserIds).toEqual([away]);
  });

  it('an active recipient is pushed when the option is off', async () => {
    const holder: { focus: { active: string[]; viewing: string[] } } = { focus: { active: [], viewing: [] } };
    const store: ConversationFocusStore = { set: async () => undefined, clear: async () => undefined, snapshot: async () => holder.focus };
    const { others, send } = setup('Direct', { others: 1 }, { conversationFocus: store, chatPushSuppressWhenActive: false });
    holder.focus = { active: [others[0]!], viewing: [] };

    const event = await send();

    expect(event.pushRecipientUserIds).toEqual([others[0]]);
  });

  it('a focus store failure does not block the send and pushes everyone', async () => {
    const failing: ConversationFocusStore = {
      set: async () => undefined,
      clear: async () => undefined,
      snapshot: async () => {
        throw new Error('redis down');
      },
    };
    const { others, send } = setup('Group', {}, { conversationFocus: failing });

    const event = await send();

    expect(event.pushRecipientUserIds).toEqual(others);
  });
});
