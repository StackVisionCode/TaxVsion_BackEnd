import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { PrismaConversationRepository } from '../../src/infrastructure/persistence/prisma-conversation-repository.js';
import { PrismaConversationActivityReader } from '../../src/infrastructure/persistence/prisma-conversation-activity-reader.js';

/** Bandeja de Support: no leídos (mensajes ajenos después de la última lectura) y último mensaje, con SQL real. */
const prisma = new PrismaClient();
const conversations = new PrismaConversationRepository(prisma);
const reader = new PrismaConversationActivityReader(prisma);
const tenantId = randomUUID();
const me = randomUUID();
const agent = randomUUID();
let busyId = '';
let quietId = '';

function group(title: string) {
  const conv = Conversation.startGroup({
    tenantId,
    groupId: randomUUID(),
    title,
    creator: { userId: me, displayName: 'Me', actorType: 'TenantEmployee' },
    members: [{ userId: agent, displayName: 'Agent', actorType: 'TenantEmployee' }],
  });
  if (!conv.isSuccess) throw new Error(conv.error.code);
  return conv.value;
}

beforeAll(async () => {
  const busy = group('Busy');
  busy.sendText({ senderId: me, body: 'Help please' });
  busy.sendText({ senderId: agent, body: 'On it' });
  busy.sendText({ senderId: agent, body: 'Fixed' });
  busyId = busy.id;
  await conversations.save(busy);

  const quiet = group('Quiet');
  quiet.sendText({ senderId: agent, body: 'Old reply' });
  quiet.sendText({ senderId: me, body: 'Thanks' });
  quietId = quiet.id;
  await conversations.save(quiet);
  // Horas distintas: creados en el mismo milisegundo, el orden no sería el real.
  const order = ['Help please', 'On it', 'Fixed', 'Old reply', 'Thanks'];
  for (const [index, body] of order.entries()) {
    await prisma.message.updateMany({
      where: { TenantId: tenantId, Body: body },
      data: { CreatedAtUtc: new Date(Date.UTC(2026, 9, 31, 12, index)) },
    });
  }
  // Ya leí todo lo de "Quiet".
  await prisma.conversationParticipant.updateMany({
    where: { ConversationId: quietId, UserId: me },
    data: { LastReadAtUtc: new Date(Date.now() + 60_000) },
  });
});

afterAll(async () => {
  await prisma.conversation.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('PrismaConversationActivityReader (SQL real)', () => {
  it('cuenta solo lo ajeno sin leer y devuelve el último mensaje de cada conversación', async () => {
    const activity = await reader.readForUser({ tenantId, userId: me, conversationIds: [busyId, quietId] });

    expect(activity.get(busyId)?.unreadCount).toBe(2);
    expect(activity.get(busyId)?.lastMessage).toMatchObject({ senderId: agent, body: 'Fixed', kind: 'Text' });
    expect(activity.get(quietId)?.unreadCount).toBe(0);
    expect(activity.get(quietId)?.lastMessage).toMatchObject({ senderId: me, body: 'Thanks' });
  });

  it('una lista vacía no consulta', async () => {
    expect((await reader.readForUser({ tenantId, userId: me, conversationIds: [] })).size).toBe(0);
  });
});
