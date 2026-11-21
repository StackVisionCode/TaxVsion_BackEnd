import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaFileLibraryReadRepository } from '../../src/infrastructure/persistence/prisma-file-library-read-repository.js';

/** Biblioteca de archivos contra SQL Server real: ámbito, visibilidad y backfill de los adjuntos de chat. */
const prisma = new PrismaClient();
const repo = new PrismaFileLibraryReadRepository(prisma);
const tenantId = randomUUID();
const otherTenantId = randomUUID();
const me = randomUUID();
const otherStaff = randomUUID();
const clientUser = randomUUID();
const customerId = randomUUID();
const ids = {
  member: randomUUID(),
  left: randomUUID(),
  deletedOnly: randomUUID(),
  pendingOnly: randomUUID(),
  closed: randomUUID(),
  foreign: randomUUID(),
};
const files = {
  live: randomUUID(),
  voice: randomUUID(),
  transcript: randomUUID(),
  purged: randomUUID(),
  inLeft: randomUUID(),
  deleted: randomUUID(),
  pending: randomUUID(),
  closed: randomUUID(),
  foreign: randomUUID(),
};

async function conversation(
  id: string,
  kind: 'Group' | 'Direct',
  participants: { userId: string; actorType?: string; isRemoved?: boolean; customerId?: string }[],
  opts: { tenant?: string; archived?: boolean; lastMessageAtUtc?: Date } = {},
) {
  const tenant = opts.tenant ?? tenantId;
  await prisma.conversation.create({
    data: {
      Id: id,
      TenantId: tenant,
      Kind: kind,
      UniquenessKey: kind === 'Group' ? `group:${randomUUID()}` : `direct:${randomUUID()}:${randomUUID()}`,
      IsArchived: opts.archived ?? false,
      LastMessageAtUtc: opts.lastMessageAtUtc ?? null,
      CreatedByUserId: participants[0]?.userId ?? me,
      Participants: {
        create: participants.map((p) => ({
          TenantId: tenant,
          UserId: p.userId,
          DisplayName: 'User',
          ActorType: p.actorType ?? 'TenantEmployee',
          IsRemoved: p.isRemoved ?? false,
          CustomerId: p.customerId ?? null,
        })),
      },
    },
  });
}

async function attachment(
  conversationId: string,
  fileId: string,
  opts: { status?: string; deleted?: boolean; voice?: boolean; noMessage?: boolean; tenant?: string } = {},
) {
  const tenant = opts.tenant ?? tenantId;
  const messageId = randomUUID();
  if (!opts.noMessage) {
    await prisma.message.create({
      data: {
        Id: messageId,
        ConversationId: conversationId,
        TenantId: tenant,
        SenderId: me,
        SenderDisplayName: 'Me',
        Kind: 'Attachment',
        AttachmentFileId: fileId,
        IsDeleted: opts.deleted ?? false,
        AudioDurationMs: opts.voice ? 3_000 : null,
      },
    });
  }
  await prisma.attachmentTracking.create({
    data: {
      FileId: fileId,
      MessageId: messageId,
      ConversationId: conversationId,
      TenantId: tenant,
      Status: opts.status ?? 'Available',
    },
  });
}

beforeAll(async () => {
  await conversation(ids.member, 'Group', [{ userId: me }, { userId: otherStaff }], {
    lastMessageAtUtc: new Date('2026-11-20T10:00:00Z'),
  });
  await attachment(ids.member, files.live);
  await attachment(ids.member, files.voice, { voice: true });
  await attachment(ids.member, files.transcript);
  await attachment(ids.member, files.purged, { noMessage: true });
  await prisma.transcriptArtifact.create({
    data: {
      TenantId: tenantId,
      Scope: 'Call',
      ScopeId: randomUUID(),
      State: 'Ready',
      TxtFileId: files.transcript,
    },
  });

  await conversation(ids.left, 'Group', [{ userId: otherStaff }, { userId: me, isRemoved: true }]);
  await attachment(ids.left, files.inLeft);
  await conversation(ids.deletedOnly, 'Group', [{ userId: me }, { userId: otherStaff }]);
  await attachment(ids.deletedOnly, files.deleted, { deleted: true });
  await conversation(ids.pendingOnly, 'Group', [{ userId: me }, { userId: otherStaff }]);
  await attachment(ids.pendingOnly, files.pending, { status: 'Pending' });

  // Direct de cliente cerrado por el retiro del staff: archivado, staff fuera, cliente vigente.
  await conversation(
    ids.closed,
    'Direct',
    [
      { userId: otherStaff, isRemoved: true },
      { userId: clientUser, actorType: 'CustomerPortal', customerId },
    ],
    { archived: true, lastMessageAtUtc: new Date('2026-11-20T11:00:00Z') },
  );
  await attachment(ids.closed, files.closed);

  await conversation(ids.foreign, 'Group', [{ userId: me }, { userId: otherStaff }], {
    tenant: otherTenantId,
  });
  await attachment(ids.foreign, files.foreign, { tenant: otherTenantId });
});

afterAll(async () => {
  for (const t of [tenantId, otherTenantId]) {
    await prisma.attachmentTracking.deleteMany({ where: { TenantId: t } });
    await prisma.transcriptArtifact.deleteMany({ where: { TenantId: t } });
    await prisma.customerAssignmentProjection.deleteMany({ where: { TenantId: t } });
    await prisma.conversation.deleteMany({ where: { TenantId: t } });
  }
  await prisma.$disconnect();
});

describe('PrismaFileLibraryReadRepository (SQL real)', () => {
  it('el ámbito de miembro trae solo chats activos con un adjunto disponible de un mensaje vivo', async () => {
    const scope = await repo.listMemberChatScope({ tenantId, userId: me, take: 10 });

    expect(scope.map((r) => r.conversationId)).toEqual([ids.member]);
  });

  it('los chats de cliente cerrados: todos con view_all; con asignación solo si el cliente está asignado', async () => {
    const all = await repo.listClosedClientChatScope({ tenantId, userId: me, assignedOnly: false, take: 10 });
    const before = await repo.listClosedClientChatScope({
      tenantId,
      userId: me,
      assignedOnly: true,
      take: 10,
    });
    await prisma.customerAssignmentProjection.create({
      data: { TenantId: tenantId, CustomerId: customerId, UserId: me, Version: new Date() },
    });
    const after = await repo.listClosedClientChatScope({
      tenantId,
      userId: me,
      assignedOnly: true,
      take: 10,
    });

    expect(all.map((r) => r.conversationId)).toEqual([ids.closed]);
    expect(before).toEqual([]);
    expect(after.map((r) => r.conversationId)).toEqual([ids.closed]);
  });

  it('la visibilidad omite mensaje borrado, purgado, pendiente, transcript y otro tenant', async () => {
    const rows = await repo.findChatAttachments({ tenantId, fileIds: Object.values(files) });

    expect(new Set(rows.map((r) => r.fileId))).toEqual(
      new Set([files.live, files.voice, files.inLeft, files.closed]),
    );
    expect(rows.find((r) => r.fileId === files.live)?.conversationId).toBe(ids.member);
  });

  it('el backfill recorre por FileId los trackings con mensaje, sin transcripts, y marca la nota de voz', async () => {
    const seen: { fileId: string; isVoiceNote: boolean }[] = [];
    let after: string | null = null;
    for (;;) {
      const page = await repo.listChatAttachmentsForRedeclare({ tenantId, afterFileId: after, take: 2 });
      seen.push(...page.map((r) => ({ fileId: r.fileId, isVoiceNote: r.isVoiceNote })));
      if (page.length < 2) break;
      after = page.at(-1)?.fileId ?? null;
    }

    expect(new Set(seen.map((s) => s.fileId))).toEqual(
      new Set([files.live, files.voice, files.inLeft, files.deleted, files.pending, files.closed]),
    );
    expect(seen).toHaveLength(6);
    expect(seen.filter((s) => s.isVoiceNote).map((s) => s.fileId)).toEqual([files.voice]);
  });

  it('el reclamo por ids usa el criterio del backfill y no cruza tenant', async () => {
    const rows = await repo.findChatAttachmentsForRedeclare({
      tenantId,
      fileIds: [...Object.values(files), randomUUID()],
    });

    expect(new Set(rows.map((r) => r.fileId))).toEqual(
      new Set([files.live, files.voice, files.inLeft, files.deleted, files.pending, files.closed]),
    );
    expect(rows.filter((r) => r.isVoiceNote).map((r) => r.fileId)).toEqual([files.voice]);
  });

  it('los clientes de una conversación salen con su cliente', async () => {
    const rows = await repo.listConversationClients({ tenantId, conversationIds: [ids.closed, ids.member] });

    expect(rows).toEqual([
      { conversationId: ids.closed, actorType: 'CustomerPortal', customerId, isRemoved: false },
    ]);
  });
});
