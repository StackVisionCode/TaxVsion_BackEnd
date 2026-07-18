import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Message } from '../../src/domain/conversations/message.js';
import { MessageReaction } from '../../src/domain/conversations/message-reaction.js';

function u(): string {
  return randomUUID();
}

function makeText(): Message {
  const r = Message.createText({
    conversationId: u(),
    tenantId: u(),
    senderId: u(),
    senderDisplayName: 'Alice',
    body: 'hola mundo',
  });
  if (!r.isSuccess) throw new Error();
  return r.value;
}

describe('Message.pin / unpin (Fase 9)', () => {
  it('pin sets isPinned + timestamps, unpin clears them', () => {
    const m = makeText();
    const pinner = u();
    m.pin(pinner, new Date('2026-07-16T12:00:00Z'));
    const s = m.toSnapshot();
    expect(s.isPinned).toBe(true);
    expect(s.pinnedByUserId).toBe(pinner);
    expect(s.pinnedAtUtc?.toISOString()).toBe('2026-07-16T12:00:00.000Z');

    m.unpin();
    const s2 = m.toSnapshot();
    expect(s2.isPinned).toBe(false);
    expect(s2.pinnedAtUtc).toBeNull();
    expect(s2.pinnedByUserId).toBeNull();
  });

  it('pin on a deleted message is silently ignored', () => {
    const m = makeText();
    m.softDelete(m.senderId, false);
    m.pin(u());
    expect(m.toSnapshot().isPinned).toBe(false);
  });

  it('pin on an already-pinned message is idempotent (no double-set)', () => {
    const m = makeText();
    const t1 = new Date('2026-07-16T12:00:00Z');
    m.pin(u(), t1);
    const first = m.toSnapshot().pinnedAtUtc;
    m.pin(u(), new Date('2026-07-16T13:00:00Z'));
    expect(m.toSnapshot().pinnedAtUtc?.toISOString()).toBe(first?.toISOString());
  });
});

describe('Message.createForwarded (Fase 9)', () => {
  it('copies body + attachment from origin, sets forwardedFromMessageId, senderId = forwarder', () => {
    const origin = makeText();
    const originSnap = origin.toSnapshot();
    const forwarder = u();
    const result = Message.createForwarded({
      conversationId: u(),
      tenantId: originSnap.tenantId,
      forwarderUserId: forwarder,
      forwarderDisplayName: 'Bob',
      origin: originSnap,
    });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    const s = result.value.toSnapshot();
    expect(s.senderId).toBe(forwarder);
    expect(s.senderDisplayName).toBe('Bob');
    expect(s.body).toBe(originSnap.body);
    expect(s.forwardedFromMessageId).toBe(originSnap.id);
    expect(s.id).not.toBe(originSnap.id);
    expect(s.isPinned).toBe(false);
    expect(s.replyToMessageId).toBeNull();
  });

  it('rejects forwarding a deleted message', () => {
    const origin = makeText();
    origin.softDelete(origin.senderId, false);
    const result = Message.createForwarded({
      conversationId: u(),
      tenantId: origin.toSnapshot().tenantId,
      forwarderUserId: u(),
      forwarderDisplayName: 'X',
      origin: origin.toSnapshot(),
    });
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Chat.Message.ForwardDeleted');
  });
});

describe('MessageReaction.create (Fase 9)', () => {
  it('trims and creates', () => {
    const r = MessageReaction.create({ messageId: u(), tenantId: u(), userId: u(), emoji: '  👍  ' });
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value.emoji).toBe('👍');
  });

  it('rejects empty emoji', () => {
    const r = MessageReaction.create({ messageId: u(), tenantId: u(), userId: u(), emoji: '   ' });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Chat.Reaction.EmptyEmoji');
  });

  it('rejects emoji longer than 16 chars (guardrail vs text spam disguised as emoji)', () => {
    const r = MessageReaction.create({
      messageId: u(),
      tenantId: u(),
      userId: u(),
      emoji: '👍'.repeat(20),
    });
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Chat.Reaction.EmojiTooLong');
  });
});
