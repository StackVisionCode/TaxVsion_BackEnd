import { Prisma, type PrismaClient } from '@prisma/client';
import type {
  ConversationActivity,
  ConversationActivityReader,
} from '../../application/ports/conversation-activity-reader.js';

/** Dos consultas para toda la página: no leídos (misma regla que los badges) y último mensaje por conversación. */
export class PrismaConversationActivityReader implements ConversationActivityReader {
  constructor(private readonly prisma: PrismaClient) {}

  async readForUser(input: {
    tenantId: string;
    userId: string;
    conversationIds: readonly string[];
  }): Promise<ReadonlyMap<string, ConversationActivity>> {
    const result = new Map<string, ConversationActivity>();
    if (input.conversationIds.length === 0) return result;
    const ids = Prisma.join(input.conversationIds.map((id) => Prisma.sql`TRY_CONVERT(UNIQUEIDENTIFIER, ${id})`));

    const [unreadRows, lastRows] = await Promise.all([
      this.prisma.$queryRaw<{ ConversationId: string; Unread: number | bigint }[]>`
        SELECT m.[ConversationId] AS ConversationId, COUNT_BIG(m.[Id]) AS Unread
        FROM [dbo].[Message] m
        JOIN [dbo].[ConversationParticipant] p
          ON p.[ConversationId] = m.[ConversationId] AND p.[TenantId] = m.[TenantId]
         AND p.[UserId] = ${input.userId} AND p.[IsRemoved] = 0
        WHERE m.[TenantId] = ${input.tenantId} AND m.[ConversationId] IN (${ids})
          AND m.[SenderId] <> ${input.userId} AND m.[IsDeleted] = 0
          AND (p.[LastReadAtUtc] IS NULL OR m.[CreatedAtUtc] > p.[LastReadAtUtc])
        GROUP BY m.[ConversationId]`,
      this.prisma.$queryRaw<
        { ConversationId: string; SenderId: string; Kind: string; Body: string | null; IsDeleted: boolean; CreatedAtUtc: Date }[]
      >`
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
            }
          : null,
      });
    }
    return result;
  }
}
