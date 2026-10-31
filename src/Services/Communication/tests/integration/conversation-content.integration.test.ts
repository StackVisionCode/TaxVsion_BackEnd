import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { PrismaConversationRepository } from '../../src/infrastructure/persistence/prisma-conversation-repository.js';
import { PrismaMessageRepository } from '../../src/infrastructure/persistence/prisma-message-repository.js';
import { PrismaConversationContentRepository } from '../../src/infrastructure/persistence/prisma-conversation-content-repository.js';
import { PrismaAttachmentTrackingRepository } from '../../src/infrastructure/persistence/prisma-attachment-tracking-repository.js';

/** F8d contra SQL Server real: enlaces al guardar y al editar, y las pestañas de Info paginadas. */
const prisma = new PrismaClient();
const conversations = new PrismaConversationRepository(prisma);
const messages = new PrismaMessageRepository(prisma);
const content = new PrismaConversationContentRepository(prisma);
const tracking = new PrismaAttachmentTrackingRepository(prisma);
const tenantId = randomUUID();
let conversationId = '';
let textMessageId = '';

beforeAll(async () => {
  const conv = Conversation.startGroup({
    tenantId,
    groupId: randomUUID(),
    title: 'Content test',
    creator: { userId: randomUUID(), displayName: 'Ana', actorType: 'TenantEmployee' },
    members: [{ userId: randomUUID(), displayName: 'Beto', actorType: 'TenantEmployee' }],
  });
  if (!conv.isSuccess) throw new Error(conv.error.code);
  const sender = conv.value.getParticipantSnapshots()[0]?.userId ?? '';
  const sent = conv.value.sendText({ senderId: sender, body: 'Forms: https://irs.gov/a https://irs.gov/b https://irs.gov/c' });
  if (!sent.isSuccess) throw new Error(sent.error.code);
  textMessageId = sent.value.id;
  await conversations.save(conv.value);
  conversationId = conv.value.id;
  // Adjuntos: mensajes sueltos con su tracking y metadata (dos imágenes y un PDF, en segundos distintos).
  for (let i = 0; i < 3; i++) {
    const id = randomUUID();
    const fileId = randomUUID();
    await prisma.message.create({
      data: { Id: id, ConversationId: conversationId, TenantId: tenantId, SenderId: sender, SenderDisplayName: 'Ana', Kind: 'Attachment', AttachmentFileId: fileId, CreatedAtUtc: new Date(Date.now() + (i + 1) * 1000) },
    });
    await tracking.register({ fileId, messageId: id, conversationId, tenantId });
    await tracking.recordMetadata({ fileId, contentType: i === 2 ? 'application/pdf' : 'image/png', fileName: `f${i}`, sizeBytes: 10, category: i === 2 ? 'Document' : 'Image' });
  }
});

afterAll(async () => {
  await prisma.attachmentTracking.deleteMany({ where: { TenantId: tenantId } });
  await prisma.conversation.deleteMany({ where: { TenantId: tenantId } });
  await prisma.$disconnect();
});

describe('contenido de la conversación (SQL real)', () => {
  it('los enlaces se guardan con el mensaje y se recorren de a 2 sin saltar los de la misma fecha', async () => {
    const first = await content.listLinks({ tenantId, conversationId, take: 2 });
    const last = first.at(-1);
    const second = last ? await content.listLinks({ tenantId, conversationId, take: 2, before: { at: last.createdAtUtc, id: last.id } }) : [];
    expect([...first, ...second].map((l) => l.url).sort()).toEqual(['https://irs.gov/a', 'https://irs.gov/b', 'https://irs.gov/c']);
  });

  it('editar reescribe los enlaces; borrar el mensaje los oculta', async () => {
    const message = await messages.findById(tenantId, textMessageId);
    if (!message) throw new Error('missing');
    const edit = message.editText('Now only https://example.com', message.toSnapshot().senderId, { windowMinutes: 15, maxEdits: 5 }, new Date());
    if (!edit.isSuccess) throw new Error(edit.error.code);
    await messages.recordEdit(tenantId, message, { revision: edit.value.revision, previousBody: edit.value.previousBody, editedByUserId: message.toSnapshot().senderId, editedAtUtc: new Date() });
    expect((await content.listLinks({ tenantId, conversationId, take: 10 })).map((l) => l.url)).toEqual(['https://example.com/']);

    await prisma.message.update({ where: { Id: textMessageId }, data: { IsDeleted: true } });
    expect(await content.listLinks({ tenantId, conversationId, take: 10 })).toEqual([]);
  });

  it('Media trae las imágenes (más nuevas primero) y Documents el PDF', async () => {
    const media = await content.listAttachments({ tenantId, conversationId, take: 10, categories: ['Image', 'Video'] });
    expect(media.map((a) => a.fileName)).toEqual(['f1', 'f0']);
    const docs = await content.listAttachments({ tenantId, conversationId, take: 10, categories: ['Document', 'Other'] });
    expect(docs.map((a) => [a.fileName, a.contentType])).toEqual([['f2', 'application/pdf']]);
  });
});
