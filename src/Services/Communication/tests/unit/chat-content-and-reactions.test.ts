import { randomUUID as u } from 'node:crypto';
import { describe, expect, it, vi } from 'vitest';
import { categorizeContentType } from '../../src/domain/attachments/attachment-category.js';
import { extractMessageLinks, MAX_LINKS_PER_MESSAGE } from '../../src/domain/conversations/message-links.js';
import { isReactionEmoji } from '../../src/domain/conversations/reaction-emoji.js';
import { MessageReaction } from '../../src/domain/conversations/message-reaction.js';
import { addMessageReaction } from '../../src/application/use-cases/add-message-reaction.js';
import { summarizeReactions } from '../../src/application/use-cases/get-messages.js';
import { listConversationContent } from '../../src/application/use-cases/list-conversation-content.js';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { effectiveDefaults } from '../helpers/fake-effective-settings.js';

/** F8d — categoría por tipo real, enlaces (T-16), validador de emoji (T-25), tope D-24, reacciones en el DTO (T-26). */
describe('F8d contenido y reacciones', () => {
  it('categoría por MIME detectado', () => {
    expect(categorizeContentType('image/png')).toBe('Image');
    expect(categorizeContentType('video/mp4; codecs=avc1')).toBe('Video');
    expect(categorizeContentType('audio/webm')).toBe('Audio');
    expect(categorizeContentType('application/pdf')).toBe('Document');
    expect(categorizeContentType('application/x-msdownload')).toBe('Other');
    expect(categorizeContentType(null)).toBe('Other');
  });

  it('enlaces: http(s) y www., sin puntuación final, sin javascript:, sin credenciales, sin repetidos y con tope', () => {
    const links = extractMessageLinks('See https://irs.gov/forms. Also www.example.com/a?b=1) and javascript:alert(1) or https://user:pw@evil.com x https://irs.gov/forms');
    expect(links).toEqual([
      { url: 'https://irs.gov/forms', domain: 'irs.gov' },
      { url: 'https://www.example.com/a?b=1', domain: 'example.com' },
    ]);
    const many = Array.from({ length: 15 }, (_, i) => `https://site${i}.com`).join(' ');
    expect(extractMessageLinks(many)).toHaveLength(MAX_LINKS_PER_MESSAGE);
    expect(extractMessageLinks('no links here')).toEqual([]);
  });

  it('emoji: uno solo (con tono o ZWJ); nunca texto ni dos', () => {
    expect(isReactionEmoji('👍')).toBe(true);
    expect(isReactionEmoji('👍🏽')).toBe(true);
    expect(isReactionEmoji('👨‍👩‍👧')).toBe(true);
    expect(isReactionEmoji('🇺🇸')).toBe(true);
    expect(isReactionEmoji('ok')).toBe(false);
    expect(isReactionEmoji('👍👍')).toBe(false);
    expect(isReactionEmoji('a👍')).toBe(false);
    const r = MessageReaction.create({ messageId: u(), tenantId: u(), userId: u(), emoji: 'lol' });
    expect(r.isSuccess ? 'ok' : r.error.code).toBe('Chat.Reaction.InvalidEmoji');
  });

  it('D-24: como mucho N emojis distintos por persona y mensaje; repetir uno puesto no cuenta', async () => {
    const tenantId = u();
    const conv = Conversation.startGroup({
      tenantId, groupId: u(), title: 'G',
      creator: { userId: 'me', displayName: 'Me', actorType: 'TenantEmployee' },
      members: [{ userId: 'ana', displayName: 'Ana', actorType: 'TenantEmployee' }],
    });
    if (!conv.isSuccess) throw new Error(conv.error.code);
    const sent = conv.value.sendText({ senderId: 'ana', body: 'hi' });
    if (!sent.isSuccess) throw new Error(sent.error.code);
    const existing = ['👍', '❤️', '😂'].map((emoji) => ({ id: u(), messageId: sent.value.id, tenantId, userId: 'me', emoji, createdAtUtc: new Date() }));
    const addReaction = vi.fn(async () => ({ wasNew: true }));
    const deps = {
      messages: { findById: async () => sent.value, listReactionsByMessage: async () => existing, addReaction } as never,
      conversations: { findById: async () => conv.value } as never,
      emitter: { emitToConversation: vi.fn() } as never,
      effectiveSettings: { resolve: async () => effectiveDefaults(tenantId) },
    };
    const cmd = { tenantId, correlationId: 'c', messageId: sent.value.id, userId: 'me' };
    const over = await addMessageReaction({ ...cmd, emoji: '🎉' }, deps);
    expect(over.isSuccess ? 'ok' : over.error.code).toBe('Chat.Reaction.LimitReached');
    expect(addReaction).not.toHaveBeenCalled();
    expect((await addMessageReaction({ ...cmd, emoji: '👍' }, deps)).isSuccess).toBe(true);
  });

  it('T-26: resumen por mensaje y emoji con quién reaccionó', () => {
    const map = summarizeReactions([
      { messageId: 'm1', emoji: '👍', userId: 'a' },
      { messageId: 'm1', emoji: '❤️', userId: 'b' },
      { messageId: 'm1', emoji: '👍', userId: 'b' },
      { messageId: 'm2', emoji: '😂', userId: 'a' },
    ]);
    expect(map.get('m1')).toEqual([
      { emoji: '👍', count: 2, userIds: ['a', 'b'] },
      { emoji: '❤️', count: 1, userIds: ['b'] },
    ]);
    expect(map.get('m2')).toHaveLength(1);
  });

  it('pestañas de Info: participantes, D-21 para el cliente y cursor fecha|id', async () => {
    const tenantId = u();
    const conv = Conversation.startGroup({
      tenantId, groupId: u(), title: 'G',
      creator: { userId: 'staff', displayName: 'S', actorType: 'TenantEmployee' },
      members: [{ userId: 'client', displayName: 'C', actorType: 'CustomerPortal', customerId: 'cu' }],
    });
    if (!conv.isSuccess) throw new Error(conv.error.code);
    const listLinks = vi.fn(async () => [{ id: '00000000-0000-0000-0000-000000000001', messageId: 'm', senderId: 'staff', url: 'https://a.com/', domain: 'a.com', createdAtUtc: new Date('2026-10-31T10:00:00Z') }]);
    const listAttachments = vi.fn(async () => []);
    const deps = { conversations: { findById: async () => conv.value } as never, conversationContent: { listLinks, listAttachments } };

    const denied = await listConversationContent({ tenantId, conversationId: conv.value.id, requesterUserId: 'stranger', tab: 'Links' }, deps);
    expect(denied.isSuccess ? 'ok' : denied.error.code).toBe('Chat.Conversation.NotParticipant');

    const page = await listConversationContent({ tenantId, conversationId: conv.value.id, requesterUserId: 'client', tab: 'Links', take: 1 }, deps);
    expect(listLinks).toHaveBeenCalledWith(expect.objectContaining({ notBeforeUtc: expect.any(Date), before: null }));
    expect(page.isSuccess && page.value.nextBefore).toBe('2026-10-31T10:00:00.000Z|00000000-0000-0000-0000-000000000001');

    await listConversationContent({ tenantId, conversationId: conv.value.id, requesterUserId: 'staff', tab: 'Media', before: '2026-10-31T10:00:00.000Z|00000000-0000-0000-0000-000000000001' }, deps);
    expect(listAttachments).toHaveBeenCalledWith(expect.objectContaining({ categories: ['Image', 'Video'], notBeforeUtc: null, before: { at: new Date('2026-10-31T10:00:00Z'), id: '00000000-0000-0000-0000-000000000001' } }));
  });
});
