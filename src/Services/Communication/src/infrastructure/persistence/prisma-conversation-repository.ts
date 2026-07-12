import type { PrismaClient } from '@prisma/client';
import type { Conversation, ConversationSnapshot } from '../../domain/conversations/conversation.js';
import type { MessageSnapshot } from '../../domain/conversations/message.js';
import type { ConversationRepository } from '../../application/ports/conversation-repository.js';
import { toDomainConversation, toDomainMessage, toDomainParticipant } from './conversation-mapper.js';

/**
 * Repositorio Prisma para el aggregate Conversation. Reglas:
 *   - Filtro TenantId obligatorio en TODA query — cierre CRIT-legacy.
 *   - Save = transaccion unica (root + participants + pending messages).
 *   - findById puede omitir historial (limit=0) para operaciones mutadoras
 *     que solo necesitan participants; los reads paginan aparte.
 */
export class PrismaConversationRepository implements ConversationRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async save(conversation: Conversation): Promise<void> {
    const snapshot = conversation.toSnapshot();
    const pending = conversation.drainPendingMessages().map((m) => m.toSnapshot());

    await this.prisma.$transaction(async (tx) => {
      await tx.conversation.upsert({
        where: { Id: snapshot.id },
        create: {
          Id: snapshot.id,
          TenantId: snapshot.tenantId,
          Kind: snapshot.kind,
          Title: snapshot.title,
          UniquenessKey: snapshot.uniquenessKey,
          IsArchived: snapshot.isArchived,
          LastMessageAtUtc: snapshot.lastMessageAtUtc,
          CreatedByUserId: snapshot.createdByUserId,
          CreatedAtUtc: snapshot.createdAtUtc,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
        update: {
          Title: snapshot.title,
          IsArchived: snapshot.isArchived,
          LastMessageAtUtc: snapshot.lastMessageAtUtc,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
      });

      for (const p of snapshot.participants) {
        await tx.conversationParticipant.upsert({
          where: {
            ConversationId_UserId: { ConversationId: snapshot.id, UserId: p.userId },
          },
          create: {
            Id: p.id,
            ConversationId: snapshot.id,
            TenantId: p.tenantId,
            UserId: p.userId,
            DisplayName: p.displayName,
            ActorType: p.actorType,
            Role: p.role,
            IsPrimaryPreparer: p.isPrimaryPreparer,
            IsMuted: p.isMuted,
            IsRemoved: p.isRemoved,
            JoinedAtUtc: p.joinedAtUtc,
            RemovedAtUtc: p.removedAtUtc,
            LastReadAtUtc: p.lastReadAtUtc,
            LastReadMessageId: p.lastReadMessageId,
          },
          update: {
            DisplayName: p.displayName,
            IsPrimaryPreparer: p.isPrimaryPreparer,
            IsMuted: p.isMuted,
            IsRemoved: p.isRemoved,
            RemovedAtUtc: p.removedAtUtc,
            LastReadAtUtc: p.lastReadAtUtc,
            LastReadMessageId: p.lastReadMessageId,
          },
        });
      }

      if (pending.length > 0) {
        await tx.message.createMany({
          data: pending.map((m) => ({
            Id: m.id,
            ConversationId: m.conversationId,
            TenantId: m.tenantId,
            SenderId: m.senderId,
            SenderDisplayName: m.senderDisplayName,
            Kind: m.kind,
            Body: m.body,
            AttachmentFileId: m.attachmentFileId,
            ReplyToMessageId: m.replyToMessageId,
            IsEdited: m.isEdited,
            IsDeleted: m.isDeleted,
            DeletedAtUtc: m.deletedAtUtc,
            CreatedAtUtc: m.createdAtUtc,
            EditedAtUtc: m.editedAtUtc,
          })),
        });
      }
    });
  }

  async findById(tenantId: string, id: string, recentMessagesLimit = 0): Promise<Conversation | null> {
    const row = await this.prisma.conversation.findFirst({
      where: { Id: id, TenantId: tenantId },
    });
    if (!row) return null;
    const participants = await this.prisma.conversationParticipant.findMany({
      where: { ConversationId: id, TenantId: tenantId },
    });
    const recent =
      recentMessagesLimit > 0
        ? await this.prisma.message.findMany({
            where: { ConversationId: id, TenantId: tenantId },
            orderBy: { CreatedAtUtc: 'desc' },
            take: recentMessagesLimit,
          })
        : [];
    return toDomainConversation(row, participants, recent);
  }

  async findByUniquenessKey(tenantId: string, uniquenessKey: string): Promise<Conversation | null> {
    const row = await this.prisma.conversation.findFirst({
      where: { TenantId: tenantId, UniquenessKey: uniquenessKey },
    });
    if (!row) return null;
    const participants = await this.prisma.conversationParticipant.findMany({
      where: { ConversationId: row.Id, TenantId: tenantId },
    });
    return toDomainConversation(row, participants, []);
  }

  async listForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
    includeArchived?: boolean;
  }): Promise<ConversationSnapshot[]> {
    const whereBase = {
      TenantId: input.tenantId,
      Participants: {
        some: { UserId: input.userId, IsRemoved: false, TenantId: input.tenantId },
      },
    };
    const where = input.includeArchived ? whereBase : { ...whereBase, IsArchived: false };
    const rows = await this.prisma.conversation.findMany({
      where,
      include: { Participants: true },
      orderBy: [{ LastMessageAtUtc: { sort: 'desc', nulls: 'last' } }, { UpdatedAtUtc: 'desc' }],
      take: input.take,
      skip: input.skip,
    });

    return rows.map((row) => ({
      id: row.Id,
      tenantId: row.TenantId,
      kind: row.Kind as 'Direct' | 'Group' | 'Support' | 'Meeting',
      title: row.Title,
      uniquenessKey: row.UniquenessKey,
      isArchived: row.IsArchived,
      lastMessageAtUtc: row.LastMessageAtUtc,
      createdByUserId: row.CreatedByUserId,
      createdAtUtc: row.CreatedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
      participants: row.Participants.map(toDomainParticipant),
      recentMessages: [],
    }));
  }

  async countForUser(tenantId: string, userId: string, includeArchived = false): Promise<number> {
    const whereBase = {
      TenantId: tenantId,
      Participants: { some: { UserId: userId, IsRemoved: false, TenantId: tenantId } },
    };
    const where = includeArchived ? whereBase : { ...whereBase, IsArchived: false };
    return this.prisma.conversation.count({ where });
  }

  async listMessages(input: {
    tenantId: string;
    conversationId: string;
    beforeUtc?: Date;
    afterUtc?: Date;
    take: number;
  }): Promise<MessageSnapshot[]> {
    const whereBase = { TenantId: input.tenantId, ConversationId: input.conversationId };
    if (input.afterUtc) {
      const rows = await this.prisma.message.findMany({
        where: { ...whereBase, CreatedAtUtc: { gt: input.afterUtc } },
        orderBy: { CreatedAtUtc: 'asc' },
        take: input.take,
      });
      return rows.map(toDomainMessage);
    }
    const where = input.beforeUtc
      ? { ...whereBase, CreatedAtUtc: { lt: input.beforeUtc } }
      : whereBase;
    const rows = await this.prisma.message.findMany({
      where,
      orderBy: { CreatedAtUtc: 'desc' },
      take: input.take,
    });
    return rows.map(toDomainMessage);
  }

  async countUnreadForUser(input: {
    tenantId: string;
    conversationId: string;
    userId: string;
  }): Promise<number> {
    const participant = await this.prisma.conversationParticipant.findFirst({
      where: {
        ConversationId: input.conversationId,
        TenantId: input.tenantId,
        UserId: input.userId,
        IsRemoved: false,
      },
      select: { LastReadAtUtc: true },
    });
    if (!participant) return 0;
    const whereBase = {
      ConversationId: input.conversationId,
      TenantId: input.tenantId,
      SenderId: { not: input.userId },
      IsDeleted: false,
    };
    const where = participant.LastReadAtUtc
      ? { ...whereBase, CreatedAtUtc: { gt: participant.LastReadAtUtc } }
      : whereBase;
    return this.prisma.message.count({ where });
  }
}
