import { randomUUID } from 'node:crypto';
import type { PrismaClient } from '@prisma/client';
import type {
  ConversationAuditAction,
  ConversationAuditEntry,
  ConversationAuditRecord,
  ConversationAuditRepository,
} from '../../application/ports/conversation-audit-repository.js';

/** Tope de la columna Metadata (NVARCHAR(2000)): el detalle se recorta antes de cortar el JSON. */
const METADATA_MAX = 2000;

export class PrismaConversationAuditRepository implements ConversationAuditRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async append(entries: readonly ConversationAuditEntry[]): Promise<void> {
    if (entries.length === 0) return;
    await this.prisma.conversationAudit.createMany({
      data: entries.map((e) => ({
        Id: randomUUID(),
        TenantId: e.tenantId,
        ConversationId: e.conversationId,
        Action: e.action,
        ActorUserId: e.actorUserId,
        TargetUserId: e.targetUserId,
        Metadata: serializeMetadata(e.metadata),
        OccurredAtUtc: e.occurredAtUtc,
      })),
    });
  }

  async list(input: { tenantId: string; conversationId: string; take: number; beforeUtc?: Date }): Promise<ConversationAuditRecord[]> {
    const rows = await this.prisma.conversationAudit.findMany({
      where: {
        TenantId: input.tenantId,
        ConversationId: input.conversationId,
        ...(input.beforeUtc ? { OccurredAtUtc: { lt: input.beforeUtc } } : {}),
      },
      orderBy: { OccurredAtUtc: 'desc' },
      take: input.take,
    });
    return rows.map((r) => ({
      id: r.Id,
      tenantId: r.TenantId,
      conversationId: r.ConversationId,
      action: r.Action as ConversationAuditAction,
      actorUserId: r.ActorUserId,
      targetUserId: r.TargetUserId,
      metadata: parseMetadata(r.Metadata),
      occurredAtUtc: r.OccurredAtUtc,
    }));
  }
}

function serializeMetadata(metadata: ConversationAuditEntry['metadata']): string | null {
  if (!metadata) return null;
  const json = JSON.stringify(metadata);
  return json.length <= METADATA_MAX ? json : JSON.stringify({ truncated: true });
}

function parseMetadata(value: string | null): Record<string, unknown> | null {
  if (!value) return null;
  try {
    const parsed: unknown = JSON.parse(value);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}
