import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { computeDirectUniquenessKey } from '../../src/domain/conversations/uniqueness-key.js';

function u(): string {
  return randomUUID();
}

describe('Conversation.startDirect', () => {
  it('creates a Direct conversation with both participants', () => {
    const tenantId = u();
    const initiatorId = u();
    const recipientId = u();
    const result = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiatorId, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipientId, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;

    const snapshot = result.value.toSnapshot();
    expect(snapshot.kind).toBe('Direct');
    expect(snapshot.participants).toHaveLength(2);
    expect(snapshot.uniquenessKey).toBe(computeDirectUniquenessKey(initiatorId, recipientId));
  });

  it('rejects self-chat', () => {
    const userId = u();
    const result = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId, displayName: 'Ana', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.SelfChat');
  });

  it('produces the same uniquenessKey regardless of who initiated', () => {
    const a = u();
    const b = u();
    const first = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: a, displayName: 'A', actorType: 'TenantEmployee' },
      recipient: { userId: b, displayName: 'B', actorType: 'CustomerPortal' },
    });
    const second = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: b, displayName: 'B', actorType: 'CustomerPortal' },
      recipient: { userId: a, displayName: 'A', actorType: 'TenantEmployee' },
    });
    if (!first.isSuccess || !second.isSuccess) throw new Error('unexpected');
    expect(first.value.toSnapshot().uniquenessKey).toBe(second.value.toSnapshot().uniquenessKey);
  });

  it('marks the recipient as primary preparer when requested (closes Only Owner FE gap)', () => {
    const initiator = u();
    const recipient = u();
    const result = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: initiator, displayName: 'Client', actorType: 'CustomerPortal' },
      recipient: {
        userId: recipient,
        displayName: 'Preparer',
        actorType: 'TenantEmployee',
        isPrimaryPreparer: true,
      },
    });
    if (!result.isSuccess) throw new Error('unexpected');
    const preparer = result.value.getParticipantSnapshots().find((p) => p.userId === recipient);
    expect(preparer?.isPrimaryPreparer).toBe(true);
  });
});

describe('Conversation.sendText', () => {
  const tenantId = u();
  const initiator = u();
  const recipient = u();

  function newConv(): Conversation {
    const r = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiator, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipient, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    if (!r.isSuccess) throw new Error();
    return r.value;
  }

  it('appends a Text message and updates lastMessageAt', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: 'hola' });
    expect(msg.isSuccess).toBe(true);
    if (!msg.isSuccess) return;
    expect(msg.value.toSnapshot().body).toBe('hola');
    expect(conv.drainPendingMessages()).toHaveLength(1);
    expect(conv.toSnapshot().lastMessageAtUtc).not.toBeNull();
  });

  it('rejects sender that is not a participant', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: u(), body: 'trespass' });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('rejects empty body', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: '   ' });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Message.EmptyBody');
  });

  it('rejects >4000 chars body', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: 'x'.repeat(4001) });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Message.TooLong');
  });
});

describe('Message.editText / softDelete', () => {
  const tenantId = u();
  const initiator = u();
  const recipient = u();

  function firstMessage() {
    const r = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiator, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipient, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    if (!r.isSuccess) throw new Error();
    const msg = r.value.sendText({ senderId: initiator, body: 'original' });
    if (!msg.isSuccess) throw new Error();
    return msg.value;
  }

  it('only sender can edit', () => {
    const msg = firstMessage();
    const other = msg.editText('new', u());
    expect(other.isSuccess).toBe(false);
    if (other.isSuccess) return;
    expect(other.error.code).toBe('Chat.Message.EditForbidden');
  });

  it('editing marks IsEdited=true and updates body', () => {
    const msg = firstMessage();
    const edited = msg.editText('updated', initiator);
    expect(edited.isSuccess).toBe(true);
    const snap = msg.toSnapshot();
    expect(snap.body).toBe('updated');
    expect(snap.isEdited).toBe(true);
    expect(snap.editedAtUtc).not.toBeNull();
  });

  it('non-sender cannot delete unless moderator', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(u(), false);
    expect(attempt.isSuccess).toBe(false);
    if (attempt.isSuccess) return;
    expect(attempt.error.code).toBe('Chat.Message.DeleteForbidden');
  });

  it('moderator can delete other users messages', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(u(), true);
    expect(attempt.isSuccess).toBe(true);
    expect(msg.toSnapshot().isDeleted).toBe(true);
  });

  it('sender can always delete', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(initiator, false);
    expect(attempt.isSuccess).toBe(true);
  });
});
