import { messageLinkRows } from './message-link-rows.js';
import type { PrismaClient } from '@prisma/client';
import type { Conversation, ConversationSnapshot } from '../../domain/conversations/conversation.js';
import type { MessageSnapshot } from '../../domain/conversations/message.js';
import type { ConversationRepository } from '../../application/ports/conversation-repository.js';
import { toDomainConversation, toDomainMessage, toDomainParticipant } from './conversation-mapper.js';
import type { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import { extractMeetingId, extractSupportTicketId } from '../../domain/conversations/uniqueness-key.js';

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
          Description: snapshot.description ?? null,
          AvatarFileId: snapshot.avatarFileId ?? null,
          AdminsOnlyWrite: snapshot.adminsOnlyWrite ?? false,
          MeetingId: extractMeetingId(snapshot.uniquenessKey),
          SupportTicketId: extractSupportTicketId(snapshot.uniquenessKey),
          LastMessageAtUtc: snapshot.lastMessageAtUtc,
          CreatedByUserId: snapshot.createdByUserId,
          CreatedAtUtc: snapshot.createdAtUtc,
          UpdatedAtUtc: snapshot.updatedAtUtc,
        },
        update: {
          Title: snapshot.title,
          IsArchived: snapshot.isArchived,
          Description: snapshot.description ?? null,
          AvatarFileId: snapshot.avatarFileId ?? null,
          AdminsOnlyWrite: snapshot.adminsOnlyWrite ?? false,
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
            CustomerId: p.customerId ?? null,
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
            // F8b: roles, sucesión y reingreso (vuelve con un JoinedAtUtc nuevo, D-21).
            Role: p.role,
            JoinedAtUtc: p.joinedAtUtc,
            ...(p.customerId !== undefined ? { CustomerId: p.customerId } : {}),
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
            ForwardedFromMessageId: m.forwardedFromMessageId,
            IsEdited: m.isEdited,
            IsDeleted: m.isDeleted,
            IsPinned: m.isPinned,
            PinnedAtUtc: m.pinnedAtUtc,
            PinnedByUserId: m.pinnedByUserId,
            DeletedAtUtc: m.deletedAtUtc,
            CreatedAtUtc: m.createdAtUtc,
            EditedAtUtc: m.editedAtUtc,
            AudioDurationMs: m.audioDurationMs,
            AudioWaveform: m.audioWaveform,
            TranscriptText: m.transcriptText ?? null,
            TranscriptState: m.transcriptState ?? null,
            TranscriptLanguage: m.transcriptLanguage ?? null,
          })),
        });
        // F8d (T-16): enlaces de los mensajes de texto nuevos, en la misma transacción.
        const links = pending.flatMap((m) => messageLinkRows(m));
        if (links.length > 0) await tx.messageLink.createMany({ data: links });
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
    kinds?: readonly ConversationKind[];
  }): Promise<ConversationSnapshot[]> {
    const whereBase = {
      TenantId: input.tenantId,
      Participants: {
        some: { UserId: input.userId, IsRemoved: false, TenantId: input.tenantId },
      },
      ...(input.kinds && input.kinds.length > 0 ? { Kind: { in: [...input.kinds] } } : {}),
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
      description: row.Description,
      avatarFileId: row.AvatarFileId,
      adminsOnlyWrite: row.AdminsOnlyWrite,
      lastMessageAtUtc: row.LastMessageAtUtc,
      createdByUserId: row.CreatedByUserId,
      createdAtUtc: row.CreatedAtUtc,
      updatedAtUtc: row.UpdatedAtUtc,
      participants: row.Participants.map(toDomainParticipant),
      recentMessages: [],
    }));
  }

  async countForUser(
    tenantId: string,
    userId: string,
    includeArchived = false,
    kinds?: readonly ConversationKind[],
  ): Promise<number> {
    const whereBase = {
      TenantId: tenantId,
      Participants: { some: { UserId: userId, IsRemoved: false, TenantId: tenantId } },
      ...(kinds && kinds.length > 0 ? { Kind: { in: [...kinds] } } : {}),
    };
    const where = includeArchived ? whereBase : { ...whereBase, IsArchived: false };
    return this.prisma.conversation.count({ where });
  }

  async listMessages(input: {
    tenantId: string;
    conversationId: string;
    beforeUtc?: Date;
    afterUtc?: Date;
    notBeforeUtc?: Date | null;
    take: number;
  }): Promise<MessageSnapshot[]> {
    const floor = input.notBeforeUtc ?? null;
    const whereBase = { TenantId: input.tenantId, ConversationId: input.conversationId };
    if (input.afterUtc) {
      const rows = await this.prisma.message.findMany({
        where: { ...whereBase, CreatedAtUtc: { gt: floor && floor > input.afterUtc ? floor : input.afterUtc } },
        orderBy: { CreatedAtUtc: 'asc' },
        take: input.take,
      });
      return rows.map(toDomainMessage);
    }
    const where = {
      ...whereBase,
      ...(input.beforeUtc || floor
        ? { CreatedAtUtc: { ...(input.beforeUtc ? { lt: input.beforeUtc } : {}), ...(floor ? { gte: floor } : {}) } }
        : {}),
    };
    const rows = await this.prisma.message.findMany({
      where,
      orderBy: { CreatedAtUtc: 'desc' },
      take: input.take,
    });
    return rows.map(toDomainMessage);
  }

  async countUnreadByKind(tenantId: string, userId: string): Promise<Record<ConversationKind, number>> {
    // Una sola consulta para los badges (Chat = Direct+Group, Support aparte): mensajes ajenos, vivos y
    // posteriores a la última lectura del usuario, en conversaciones activas donde sigue participando.
    const rows = await this.prisma.$queryRaw<{ Kind: string; Unread: number | bigint }[]>`
      SELECT c.[Kind] AS Kind, COUNT_BIG(m.[Id]) AS Unread
      FROM [dbo].[Conversation] c
      JOIN [dbo].[ConversationParticipant] p
        ON p.[ConversationId] = c.[Id] AND p.[TenantId] = c.[TenantId] AND p.[UserId] = ${userId} AND p.[IsRemoved] = 0
       -- F8b (ADD-05 H-6): un chat silenciado no suma a los badges; su fila conserva sus no leídos.
       AND p.[IsMuted] = 0
      JOIN [dbo].[Message] m
        ON m.[ConversationId] = c.[Id] AND m.[TenantId] = c.[TenantId] AND m.[SenderId] <> ${userId} AND m.[IsDeleted] = 0
       AND (p.[LastReadAtUtc] IS NULL OR m.[CreatedAtUtc] > p.[LastReadAtUtc])
       AND (c.[Kind] <> 'Group' OR m.[CreatedAtUtc] >= p.[JoinedAtUtc])
      WHERE c.[TenantId] = ${tenantId} AND c.[IsArchived] = 0
      GROUP BY c.[Kind]`;
    const totals: Record<ConversationKind, number> = { Direct: 0, Group: 0, Support: 0, Meeting: 0 };
    for (const row of rows) {
      if (row.Kind in totals) totals[row.Kind as ConversationKind] = Number(row.Unread);
    }
    return totals;
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
      select: { LastReadAtUtc: true, JoinedAtUtc: true, Conversation: { select: { Kind: true } } },
    });
    if (!participant) return 0;
    // F8b: en grupos lo anterior al ingreso no cuenta como no leído (reingreso o alta tardía).
    const floor = participant.Conversation.Kind === 'Group' ? participant.JoinedAtUtc : null;
    const whereBase = {
      ConversationId: input.conversationId,
      TenantId: input.tenantId,
      SenderId: { not: input.userId },
      IsDeleted: false,
    };
    const since = [participant.LastReadAtUtc, floor].filter((d): d is Date => d !== null).sort((a, b) => b.getTime() - a.getTime())[0];
    const where = since ? { ...whereBase, CreatedAtUtc: participant.LastReadAtUtc && since === participant.LastReadAtUtc ? { gt: since } : { gte: since } } : whereBase;
    return this.prisma.message.count({ where });
  }
}
