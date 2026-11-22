import { Prisma, type PrismaClient } from '@prisma/client';
import type {
  ConversationActivity,
  ConversationActivityReader,
} from '../../application/ports/conversation-activity-reader.js';

type UnreadRow = { ConversationId: string; Unread: number | bigint };
type LastRow = {
  ConversationId: string;
  SenderId: string;
  Kind: string;
  Body: string | null;
  IsDeleted: boolean;
  CreatedAtUtc: Date;
  SenderDisplayName?: string;
  AudioDurationMs?: number | null;
};

/** Dos consultas para toda la página: no leídos (misma regla que los badges) y último mensaje por conversación. */
export class PrismaConversationActivityReader implements ConversationActivityReader {
  constructor(private readonly prisma: PrismaClient) {}

  async readForUser(input: {
    tenantId: string;
    userId: string;
    conversationIds: readonly string[];
    historyFloor?: boolean;
  }): Promise<ReadonlyMap<string, ConversationActivity>> {
    const result = new Map<string, ConversationActivity>();
    if (input.conversationIds.length === 0) return result;
    const ids = Prisma.join(input.conversationIds.map((id) => Prisma.sql`TRY_CONVERT(UNIQUEIDENTIFIER, ${id})`));

    const [unreadRows, lastRows] = input.historyFloor
      ? await this.readWithFloor(input.tenantId, input.userId, ids)
      : await Promise.all([
          this.prisma.$queryRaw<UnreadRow[]>`
        SELECT m.[ConversationId] AS ConversationId, COUNT_BIG(m.[Id]) AS Unread
        FROM [dbo].[Message] m
        JOIN [dbo].[ConversationParticipant] p
          ON p.[ConversationId] = m.[ConversationId] AND p.[TenantId] = m.[TenantId]
         AND p.[UserId] = ${input.userId} AND p.[IsRemoved] = 0
        WHERE m.[TenantId] = ${input.tenantId} AND m.[ConversationId] IN (${ids})
          AND m.[SenderId] <> ${input.userId} AND m.[IsDeleted] = 0
          AND (p.[LastReadAtUtc] IS NULL OR m.[CreatedAtUtc] > p.[LastReadAtUtc])
        GROUP BY m.[ConversationId]`,
          this.prisma.$queryRaw<LastRow[]>`
        SELECT x.[ConversationId], x.[SenderId], x.[Kind], x.[Body], x.[IsDeleted], x.[CreatedAtUtc]
        FROM (
          SELECT m.[ConversationId], m.[SenderId], m.[Kind], LEFT(m.[Body], 200) AS Body, m.[IsDeleted], m.[CreatedAtUtc],
                 ROW_NUMBER() OVER (PARTITION BY m.[ConversationId] ORDER BY m.[CreatedAtUtc] DESC, m.[Id] DESC) AS rn
          FROM [dbo].[Message] m
          WHERE m.[TenantId] = ${input.tenantId} AND m.[ConversationId] IN (${ids})
        ) x
        WHERE x.rn = 1`,
        ]);

    const unread = new Map(unreadRows.map((row) => [row.ConversationId.toLowerCase(), Number(row.Unread)]));
    const last = new Map(lastRows.map((row) => [row.ConversationId.toLowerCase(), row]));
    for (const id of input.conversationIds) {
      const key = id.toLowerCase();
      const row = last.get(key);
      result.set(id, {
        unreadCount: unread.get(key) ?? 0,
        lastMessage: row
          ? {
              senderId: row.SenderId.toLowerCase(),
              kind: row.Kind,
              body: row.IsDeleted ? null : row.Body,
              isDeleted: row.IsDeleted,
              createdAtUtc: row.CreatedAtUtc,
              ...(row.SenderDisplayName !== undefined ? { senderDisplayName: row.SenderDisplayName } : {}),
              ...(row.AudioDurationMs !== undefined ? { audioDurationMs: row.AudioDurationMs } : {}),
            }
          : null,
      });
    }
    return result;
  }

  /** En grupos nada anterior al ingreso cuenta ni se muestra (misma regla que countUnreadForUser). */
  private readWithFloor(tenantId: string, userId: string, ids: Prisma.Sql): Promise<[UnreadRow[], LastRow[]]> {
    return Promise.all([
      this.prisma.$queryRaw<UnreadRow[]>`
        SELECT m.[ConversationId] AS ConversationId, COUNT_BIG(m.[Id]) AS Unread
        FROM [dbo].[Message] m
        JOIN [dbo].[ConversationParticipant] p
          ON p.[ConversationId] = m.[ConversationId] AND p.[TenantId] = m.[TenantId]
         AND p.[UserId] = ${userId} AND p.[IsRemoved] = 0
        JOIN [dbo].[Conversation] c ON c.[Id] = m.[ConversationId] AND c.[TenantId] = m.[TenantId]
        WHERE m.[TenantId] = ${tenantId} AND m.[ConversationId] IN (${ids})
          AND m.[SenderId] <> ${userId} AND m.[IsDeleted] = 0
          AND (p.[LastReadAtUtc] IS NULL OR m.[CreatedAtUtc] > p.[LastReadAtUtc])
          AND (c.[Kind] <> 'Group' OR m.[CreatedAtUtc] >= p.[JoinedAtUtc])
        GROUP BY m.[ConversationId]`,
      this.prisma.$queryRaw<LastRow[]>`
        SELECT c.[Id] AS ConversationId, x.[SenderId], x.[SenderDisplayName], x.[Kind], x.[Body],
               x.[IsDeleted], x.[CreatedAtUtc], x.[AudioDurationMs]
        FROM [dbo].[Conversation] c
        JOIN [dbo].[ConversationParticipant] p
          ON p.[ConversationId] = c.[Id] AND p.[TenantId] = c.[TenantId]
         AND p.[UserId] = ${userId} AND p.[IsRemoved] = 0
        CROSS APPLY (
          SELECT TOP (1) m.[SenderId], m.[SenderDisplayName], m.[Kind], LEFT(m.[Body], 200) AS Body,
                 m.[IsDeleted], m.[CreatedAtUtc], m.[AudioDurationMs]
          FROM [dbo].[Message] m
          WHERE m.[ConversationId] = c.[Id] AND m.[TenantId] = c.[TenantId]
            AND (c.[Kind] <> 'Group' OR m.[CreatedAtUtc] >= p.[JoinedAtUtc])
          ORDER BY m.[CreatedAtUtc] DESC, m.[Id] DESC
        ) x
        WHERE c.[TenantId] = ${tenantId} AND c.[Id] IN (${ids})`,
    ]);
  }
}
