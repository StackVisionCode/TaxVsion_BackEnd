import type { PrismaClient } from '@prisma/client';
import type { MissedMessagesCandidate, MissedMessagesStore } from '../../application/ports/missed-messages-store.js';
import type { ChatMessagesMissedEvent } from '../../contracts/events/chat-events.js';
import { outboxRowOf } from './prisma-outbox-publisher.js';

type Row = {
  TenantId: string;
  ConversationId: string;
  ConversationKind: string;
  UserId: string;
  LastReadAtUtc: Date | null;
  MissedNoticeAtUtc: Date | null;
  UnreadCount: number | bigint;
  OldestUnreadAtUtc: Date;
  LatestMessageId: string;
  LatestSenderId: string;
};

/**
 * Parte de las conversaciones Direct y Group con mensajes recientes (índice de `LastMessageAtUtc`), no de
 * todos los participantes. Cuenta solo mensajes de otros, no borrados ni de sistema, desde que entró.
 * La condición de racha nueva va también en SQL para que el lote no repita siempre las mismas filas.
 */
export class PrismaMissedMessagesStore implements MissedMessagesStore {
  constructor(private readonly prisma: PrismaClient) {}

  async findCandidates(input: {
    lastMessageSinceUtc: Date;
    unreadBeforeUtc: Date;
    limit: number;
  }): Promise<readonly MissedMessagesCandidate[]> {
    const rows = await this.prisma.$queryRaw<Row[]>`
      SELECT TOP (${input.limit})
        p.[TenantId], p.[ConversationId], c.[Kind] AS ConversationKind, p.[UserId],
        p.[LastReadAtUtc], p.[MissedNoticeAtUtc],
        u.[UnreadCount], u.[OldestUnreadAtUtc], l.[Id] AS LatestMessageId, l.[SenderId] AS LatestSenderId
      FROM [dbo].[Conversation] c
      JOIN [dbo].[ConversationParticipant] p
        ON p.[ConversationId] = c.[Id] AND p.[TenantId] = c.[TenantId] AND p.[IsRemoved] = 0 AND p.[IsMuted] = 0
      CROSS APPLY (
        SELECT COUNT_BIG(m.[Id]) AS UnreadCount, MIN(m.[CreatedAtUtc]) AS OldestUnreadAtUtc
        FROM [dbo].[Message] m
        WHERE m.[ConversationId] = c.[Id] AND m.[TenantId] = c.[TenantId]
          AND m.[SenderId] <> p.[UserId] AND m.[IsDeleted] = 0 AND m.[Kind] <> 'System'
          AND m.[CreatedAtUtc] >= p.[JoinedAtUtc]
          AND (p.[LastReadAtUtc] IS NULL OR m.[CreatedAtUtc] > p.[LastReadAtUtc])
      ) u
      CROSS APPLY (
        SELECT TOP 1 m.[Id], m.[SenderId]
        FROM [dbo].[Message] m
        WHERE m.[ConversationId] = c.[Id] AND m.[TenantId] = c.[TenantId]
          AND m.[SenderId] <> p.[UserId] AND m.[IsDeleted] = 0 AND m.[Kind] <> 'System'
        ORDER BY m.[CreatedAtUtc] DESC, m.[Id] DESC
      ) l
      WHERE c.[Kind] IN ('Direct', 'Group') AND c.[IsArchived] = 0
        AND c.[LastMessageAtUtc] >= ${input.lastMessageSinceUtc}
        AND u.[UnreadCount] > 0 AND u.[OldestUnreadAtUtc] <= ${input.unreadBeforeUtc}
        AND (p.[MissedNoticeAtUtc] IS NULL
             OR (u.[OldestUnreadAtUtc] > p.[MissedNoticeAtUtc] AND p.[LastReadAtUtc] >= p.[MissedNoticeAtUtc]))
      ORDER BY u.[OldestUnreadAtUtc]`;
    return rows.map((row) => ({
      tenantId: row.TenantId.toLowerCase(),
      conversationId: row.ConversationId.toLowerCase(),
      conversationKind: row.ConversationKind === 'Group' ? 'Group' : 'Direct',
      userId: row.UserId.toLowerCase(),
      unreadCount: Number(row.UnreadCount),
      oldestUnreadAtUtc: row.OldestUnreadAtUtc,
      latestMessageId: row.LatestMessageId.toLowerCase(),
      latestSenderId: row.LatestSenderId.toLowerCase(),
      lastReadAtUtc: row.LastReadAtUtc,
      missedNoticeAtUtc: row.MissedNoticeAtUtc,
    }));
  }

  /** Una unidad por transacción: la marca y el evento se guardan juntos (o ninguno). */
  async recordNotice(input: {
    candidate: MissedMessagesCandidate;
    noticedAtUtc: Date;
    event: ChatMessagesMissedEvent;
  }): Promise<boolean> {
    const { candidate } = input;
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.$executeRaw`
        UPDATE [dbo].[ConversationParticipant]
        SET [MissedNoticeAtUtc] = ${input.noticedAtUtc}
        WHERE [TenantId] = ${candidate.tenantId} AND [ConversationId] = ${candidate.conversationId}
          AND [UserId] = ${candidate.userId}
          AND ([MissedNoticeAtUtc] IS NULL OR [MissedNoticeAtUtc] < ${candidate.oldestUnreadAtUtc})`;
      if (updated === 0) return false;
      await tx.outboxMessage.create({ data: outboxRowOf(input.event) });
      return true;
    });
  }
}
