import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { MessageReaction } from '../../src/domain/conversations/message-reaction.js';
import { PrismaConversationRepository } from '../../src/infrastructure/persistence/prisma-conversation-repository.js';
import { PrismaMessageRepository } from '../../src/infrastructure/persistence/prisma-message-repository.js';

/**
 * Hallazgo del E2E de F8: con la intercalación por defecto (SQL_Latin1_General_CP1_CI_AS) todos los
 * emojis fuera del plano básico (😂, 😮, 🎉…) comparaban iguales, así que una persona solo podía poner
 * uno por mensaje y quitar uno podía borrar otro. La columna `Emoji` usa intercalación binaria.
 */
const prisma = new PrismaClient();
const conversations = new PrismaConversationRepository(prisma);
const messages = new PrismaMessageRepository(prisma);
const tenantId = randomUUID();
const userId = randomUUID();
let messageId = '';

function reaction(emoji: string): MessageReaction {
  const created = MessageReaction.create({ messageId, tenantId, userId, emoji });
  if (!created.isSuccess) throw new Error(created.error.code);
  return created.value;
}

beforeAll(async () => {
  const conv = Conversation.startGroup({
    tenantId,
    groupId: randomUUID(),
    title: 'Reactions test',
    creator: { userId, displayName: 'Ana', actorType: 'TenantEmployee' },
    members: [{ userId: randomUUID(), displayName: 'Beto', actorType: 'TenantEmployee' }],
  });
  if (!conv.isSuccess) throw new Error(conv.error.code);
  const sent = conv.value.sendText({ senderId: userId, body: 'React to me' });
  if (!sent.isSuccess) throw new Error(sent.error.code);
  messageId = sent.value.id;
  await conversations.save(conv.value);
});

afterAll(async () => {
  await prisma.conversation.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('reacciones con emojis fuera del plano básico (SQL real)', () => {
  it('guarda emojis distintos de la misma persona como reacciones distintas', async () => {
    expect((await messages.addReaction(reaction('😂'))).wasNew).toBe(true);
    expect((await messages.addReaction(reaction('😮'))).wasNew).toBe(true);
    expect((await messages.addReaction(reaction('🎉'))).wasNew).toBe(true);
    expect((await messages.addReaction(reaction('😂'))).wasNew).toBe(false);

    const stored = await messages.listReactionsByMessage(tenantId, messageId);
    expect(stored.map((r) => r.emoji).sort()).toEqual(['🎉', '😂', '😮'].sort());
  });

  it('quitar un emoji no se lleva a los otros', async () => {
    expect((await messages.removeReaction({ tenantId, messageId, userId, emoji: '😮' })).wasPresent).toBe(true);

    const stored = await messages.listReactionsByMessage(tenantId, messageId);
    expect(stored.map((r) => r.emoji).sort()).toEqual(['🎉', '😂'].sort());
  });
});
