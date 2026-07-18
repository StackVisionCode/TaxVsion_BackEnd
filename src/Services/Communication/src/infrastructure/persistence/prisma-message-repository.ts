import type { PrismaClient } from '@prisma/client';
import { Message, type MessageSnapshot } from '../../domain/conversations/message.js';
import { MessageReaction, type MessageReactionSnapshot } from '../../domain/conversations/message-reaction.js';
import type { MessageRepository } from '../../application/ports/message-repository.js';
import { toDomainMessage } from './conversation-mapper.js';

/**
 * Repositorio dedicado a mutaciones sobre un mensaje individual (edit/delete)
 * y a batches de receipts. Vive aparte del conversation-repo para evitar cargar
 * el aggregate completo cuando solo hay que tocar una fila.
 *
 * `markBatchRead` cierra el N+1 del legacy con un unico UPDATE.
 * `recordDelivered` usa `createMany` con `skipDuplicates` — evita el upsert
 * per-message que el legacy hacia al connect (patch de reconexion masiva).
 */
export class PrismaMessageRepository implements MessageRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findById(tenantId: string, messageId: string): Promise<Message | null> {
    const row = await this.prisma.message.findFirst({
      where: { Id: messageId, TenantId: tenantId },
    });
    return row ? Message.rehydrate(toDomainMessage(row)) : null;
  }

  async update(tenantId: string, message: Message): Promise<void> {
    const snapshot = message.toSnapshot();
    await this.prisma.message.update({
      where: { Id: snapshot.id },
      data: {
        Body: snapshot.body,
        IsEdited: snapshot.isEdited,
        EditedAtUtc: snapshot.editedAtUtc,
        IsDeleted: snapshot.isDeleted,
        DeletedAtUtc: snapshot.deletedAtUtc,
        IsPinned: snapshot.isPinned,
        PinnedAtUtc: snapshot.pinnedAtUtc,
        PinnedByUserId: snapshot.pinnedByUserId,
        TenantId: tenantId,
      },
    });
  }

  async insertForwarded(_tenantId: string, message: Message): Promise<void> {
    const m = message.toSnapshot();
    await this.prisma.message.create({
      data: {
        Id: m.id,
        ConversationId: m.conversationId,
        TenantId: m.tenantId,
        SenderId: m.senderId,
        SenderDisplayName: m.senderDisplayName,
        Kind: m.kind,
        Body: m.body,
        AttachmentFileId: m.attachmentFileId,
        ReplyToMessageId: m.replyToMessageId,
        ForwardedFromMessageId: m.forwardedFromMessageId,
        IsEdited: m.isEdited,
        IsDeleted: m.isDeleted,
        IsPinned: m.isPinned,
        PinnedAtUtc: m.pinnedAtUtc,
        PinnedByUserId: m.pinnedByUserId,
        DeletedAtUtc: m.deletedAtUtc,
        CreatedAtUtc: m.createdAtUtc,
        EditedAtUtc: m.editedAtUtc,
      },
    });
    await this.prisma.conversation.update({
      where: { Id: m.conversationId },
      data: { LastMessageAtUtc: m.createdAtUtc, UpdatedAtUtc: m.createdAtUtc },
    });
  }

  async addReaction(reaction: MessageReaction): Promise<{ wasNew: boolean }> {
    const s = reaction.toSnapshot();
    try {
      await this.prisma.messageReaction.create({
        data: {
          Id: s.id,
          MessageId: s.messageId,
          TenantId: s.tenantId,
          UserId: s.userId,
          Emoji: s.emoji,
          CreatedAtUtc: s.createdAtUtc,
        },
      });
      return { wasNew: true };
    } catch (err) {
      // P2002 = unique constraint violation → ya existe la reaction. Idempotente.
      const code = (err as { code?: string }).code;
      if (code === 'P2002') return { wasNew: false };
      throw err;
    }
  }

  async removeReaction(input: {
    tenantId: string;
    messageId: string;
    userId: string;
    emoji: string;
  }): Promise<{ wasPresent: boolean }> {
    const result = await this.prisma.messageReaction.deleteMany({
      where: {
        MessageId: input.messageId,
        TenantId: input.tenantId,
        UserId: input.userId,
        Emoji: input.emoji,
      },
    });
    return { wasPresent: result.count > 0 };
  }

  async listReactionsByMessage(tenantId: string, messageId: string): Promise<MessageReactionSnapshot[]> {
    const rows = await this.prisma.messageReaction.findMany({
      where: { TenantId: tenantId, MessageId: messageId },
      orderBy: { CreatedAtUtc: 'asc' },
    });
    return rows.map((r) => ({
      id: r.Id,
      messageId: r.MessageId,
      tenantId: r.TenantId,
      userId: r.UserId,
      emoji: r.Emoji,
      createdAtUtc: r.CreatedAtUtc,
    }));
  }

  async searchByBody(input: {
    tenantId: string;
    conversationId: string;
    query: string;
    limit: number;
  }): Promise<MessageSnapshot[]> {
    const rows = await this.prisma.message.findMany({
      where: {
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
        IsDeleted: false,
        Body: { contains: input.query },
      },
      orderBy: { CreatedAtUtc: 'desc' },
      take: input.limit,
    });
    return rows.map(toDomainMessage);
  }

  async markBatchRead(input: {
    tenantId: string;
    conversationId: string;
    userId: string;
    lastReadMessageId: string;
    now: Date;
  }): Promise<{ markedCount: number }> {
    const cutoff = await this.prisma.message.findFirst({
      where: {
        Id: input.lastReadMessageId,
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
      },
      select: { CreatedAtUtc: true },
    });
    if (!cutoff) return { markedCount: 0 };

    const targetMessageIds = await this.prisma.message.findMany({
      where: {
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
        SenderId: { not: input.userId },
        IsDeleted: false,
        CreatedAtUtc: { lte: cutoff.CreatedAtUtc },
      },
      select: { Id: true },
    });
    if (targetMessageIds.length === 0) return { markedCount: 0 };

    // SQL Server no soporta `skipDuplicates`. Estrategia idempotente:
    //   1) buscar los receipts existentes de este usuario en los ids objetivo;
    //   2) createMany solo con los que faltan;
    //   3) updateMany para marcar ReadAtUtc en los que estan pero sin leer.
    await this.prisma.$transaction(async (tx) => {
      for (const chunk of chunks(targetMessageIds, 200)) {
        const ids = chunk.map((m) => m.Id);
        const existing = await tx.messageReceipt.findMany({
          where: { UserId: input.userId, TenantId: input.tenantId, MessageId: { in: ids } },
          select: { MessageId: true },
        });
        const existingSet = new Set(existing.map((r) => r.MessageId));
        const missing = ids.filter((id) => !existingSet.has(id));
        if (missing.length > 0) {
          await tx.messageReceipt.createMany({
            data: missing.map((id) => ({
              MessageId: id,
              UserId: input.userId,
              TenantId: input.tenantId,
              DeliveredAtUtc: input.now,
              ReadAtUtc: input.now,
            })),
          });
        }
        await tx.messageReceipt.updateMany({
          where: {
            UserId: input.userId,
            TenantId: input.tenantId,
            MessageId: { in: ids },
            ReadAtUtc: null,
          },
          data: { ReadAtUtc: input.now },
        });
      }
    });
    return { markedCount: targetMessageIds.length };
  }

  async recordDelivered(input: {
    tenantId: string;
    conversationId: string;
    messageIds: readonly string[];
    userId: string;
    now: Date;
  }): Promise<void> {
    if (input.messageIds.length === 0) return;
    for (const chunk of chunks([...input.messageIds], 200)) {
      const existing = await this.prisma.messageReceipt.findMany({
        where: { UserId: input.userId, TenantId: input.tenantId, MessageId: { in: chunk } },
        select: { MessageId: true },
      });
      const existingSet = new Set(existing.map((r) => r.MessageId));
      const missing = chunk.filter((id) => !existingSet.has(id));
      if (missing.length === 0) continue;
      await this.prisma.messageReceipt.createMany({
        data: missing.map((id) => ({
          MessageId: id,
          UserId: input.userId,
          TenantId: input.tenantId,
          DeliveredAtUtc: input.now,
        })),
      });
    }
  }

  async listByIds(tenantId: string, ids: readonly string[]): Promise<MessageSnapshot[]> {
    if (ids.length === 0) return [];
    const rows = await this.prisma.message.findMany({
      where: { TenantId: tenantId, Id: { in: [...ids] } },
    });
    return rows.map(toDomainMessage);
  }
}

function* chunks<T>(items: T[], size: number): Generator<T[]> {
  for (let i = 0; i < items.length; i += size) yield items.slice(i, i + size);
}
