import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { PrismaConversationRepository } from '../../src/infrastructure/persistence/prisma-conversation-repository.js';
import { PrismaConversationActivityReader } from '../../src/infrastructure/persistence/prisma-conversation-activity-reader.js';

/** Lista de conversaciones: lector de actividad con piso de ingreso (historyFloor), con SQL real. */
const prisma = new PrismaClient();
const conversations = new PrismaConversationRepository(prisma);
const reader = new PrismaConversationActivityReader(prisma);
const tenantId = randomUUID();
const owner = randomUUID();
const newcomer = randomUUID();
const client = randomUUID();
const at = (minute: number, second = 0) => new Date(Date.UTC(2026, 9, 31, 12, minute, second));
const ids = { floor: '', direct: '', early: '', late: '' };

function group(title: string) {
  const conv = Conversation.startGroup({
    tenantId,
    groupId: randomUUID(),
    title,
    creator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
    members: [
      { userId: newcomer, displayName: 'Newcomer', actorType: 'TenantEmployee' },
      { userId: client, displayName: 'Client', actorType: 'CustomerPortal' },
    ],
  });
  if (!conv.isSuccess) throw new Error(conv.error.code);
  return conv.value;
}

/** Fecha fija por cuerpo: creados en el mismo milisegundo, el orden no sería el real. */
async function stamp(conversationId: string, dates: Record<string, Date>) {
  for (const [body, date] of Object.entries(dates)) {
    await prisma.message.updateMany({
      where: { TenantId: tenantId, ConversationId: conversationId, Body: body },
      data: { CreatedAtUtc: date },
    });
  }
}

async function participant(
  conversationId: string,
  userId: string,
  data: { JoinedAtUtc?: Date; LastReadAtUtc?: Date | null },
) {
  await prisma.conversationParticipant.updateMany({
    where: { TenantId: tenantId, ConversationId: conversationId, UserId: userId },
    data,
  });
}

beforeAll(async () => {
  // Grupo con 3 mensajes del dueño (T0, T1, T2); el recién llegado entra entre T1 y T2.
  const floor = group('Floor');
  for (const body of ['f0', 'f1', 'f2']) floor.sendText({ senderId: owner, body });
  ids.floor = floor.id;
  await conversations.save(floor);
  await stamp(floor.id, { f0: at(0), f1: at(1), f2: at(2) });
  await participant(floor.id, owner, { JoinedAtUtc: at(0) });
  await participant(floor.id, newcomer, { JoinedAtUtc: at(1, 30), LastReadAtUtc: null });
  // El cliente entró antes de T0 y leyó hasta después de T1.
  await participant(floor.id, client, { JoinedAtUtc: at(0, 0), LastReadAtUtc: at(1, 30) });

  // Direct: sin piso, cuenta todo lo ajeno sin leer.
  const direct = Conversation.startDirect({
    tenantId,
    initiator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
    recipient: { userId: newcomer, displayName: 'Newcomer', actorType: 'TenantEmployee' },
  });
  if (!direct.isSuccess) throw new Error(direct.error.code);
  for (const body of ['d0', 'd1']) direct.value.sendText({ senderId: owner, body });
  ids.direct = direct.value.id;
  await conversations.save(direct.value);
  await stamp(direct.value.id, { d0: at(0), d1: at(1) });

  // Solo mensajes anteriores al ingreso del recién llegado y del cliente.
  const early = group('Early');
  for (const body of ['e0', 'e1']) early.sendText({ senderId: owner, body });
  ids.early = early.id;
  await conversations.save(early);
  await stamp(early.id, { e0: at(0), e1: at(1) });
  await participant(early.id, owner, { JoinedAtUtc: at(0) });
  await participant(early.id, newcomer, { JoinedAtUtc: at(5) });
  await participant(early.id, client, { JoinedAtUtc: at(5) });

  // Un mensaje anterior y otro posterior al ingreso.
  const late = group('Late');
  for (const body of ['l0', 'after join']) late.sendText({ senderId: owner, body });
  ids.late = late.id;
  await conversations.save(late);
  await stamp(late.id, { l0: at(0), 'after join': at(10) });
  await participant(late.id, owner, { JoinedAtUtc: at(0) });
  await participant(late.id, newcomer, { JoinedAtUtc: at(5) });
  await participant(late.id, client, { JoinedAtUtc: at(5) });
});

afterAll(async () => {
  await prisma.conversation.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('PrismaConversationActivityReader con historyFloor (SQL real)', () => {
  it('en grupos, el no leído con piso coincide con countUnreadForUser', async () => {
    for (const userId of [newcomer, client, owner]) {
      const activity = await reader.readForUser({
        tenantId,
        userId,
        conversationIds: [ids.floor, ids.direct],
        historyFloor: true,
      });
      for (const conversationId of [ids.floor, ids.direct]) {
        const expected = await conversations.countUnreadForUser({ tenantId, conversationId, userId });
        expect(activity.get(conversationId)?.unreadCount ?? 0, `${userId} en ${conversationId}`).toBe(
          expected,
        );
      }
    }
    const newcomerFloor = await reader.readForUser({
      tenantId,
      userId: newcomer,
      conversationIds: [ids.floor],
      historyFloor: true,
    });
    expect(newcomerFloor.get(ids.floor)?.unreadCount).toBe(1);
  });

  it('un cliente del portal y un miembro nuevo no ven un último mensaje anterior a su ingreso', async () => {
    for (const userId of [newcomer, client]) {
      const activity = await reader.readForUser({
        tenantId,
        userId,
        conversationIds: [ids.early, ids.late],
        historyFloor: true,
      });
      expect(activity.get(ids.early)?.lastMessage ?? null).toBeNull();
      expect(activity.get(ids.late)?.lastMessage).toMatchObject({
        senderId: owner,
        senderDisplayName: 'Owner',
        kind: 'Text',
        body: 'after join',
      });
    }
    // Quien estaba desde el principio sí ve el último.
    const ownerView = await reader.readForUser({
      tenantId,
      userId: owner,
      conversationIds: [ids.early],
      historyFloor: true,
    });
    expect(ownerView.get(ids.early)?.lastMessage).toMatchObject({ body: 'e1' });
  });

  it('quien no participa no recibe actividad', async () => {
    const activity = await reader.readForUser({
      tenantId,
      userId: randomUUID(),
      conversationIds: [ids.late],
      historyFloor: true,
    });
    expect(activity.get(ids.late)).toEqual({ unreadCount: 0, lastMessage: null });
  });
});
