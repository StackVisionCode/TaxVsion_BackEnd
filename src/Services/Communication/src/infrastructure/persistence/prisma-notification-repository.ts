import type { PrismaClient } from '@prisma/client';
import { Prisma } from '@prisma/client';
import { Notification, isPriority, type NotificationSnapshot } from '../../domain/notifications/notification.js';
import type { NotificationRepository } from '../../application/ports/notification-repository.js';

export class PrismaNotificationRepository implements NotificationRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async createIfMissing(notification: Notification): Promise<boolean> {
    const s = notification.toSnapshot();
    try {
      await this.prisma.notificationEntry.create({
        data: {
          Id: s.id,
          TenantId: s.tenantId,
          UserId: s.userId,
          Kind: s.kind,
          Priority: s.priority,
          Title: s.title,
          Body: s.body,
          MetadataJson: JSON.stringify(s.metadata),
          SourceEventId: s.sourceEventId,
          SourceEventType: s.sourceEventType,
          CorrelationId: s.correlationId,
          CreatedAtUtc: s.createdAtUtc,
        },
      });
      return true;
    } catch (err) {
      if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === 'P2002') {
        // Unique constraint (TenantId, SourceEventId, UserId) — evento ya visto.
        return false;
      }
      throw err;
    }
  }

  async findById(tenantId: string, id: string, userId: string): Promise<Notification | null> {
    const row = await this.prisma.notificationEntry.findFirst({
      where: { Id: id, TenantId: tenantId, UserId: userId },
    });
    return row ? Notification.rehydrate(this.toSnapshot(row)) : null;
  }

  async update(tenantId: string, notification: Notification): Promise<void> {
    const s = notification.toSnapshot();
    await this.prisma.notificationEntry.update({
      where: { Id: s.id },
      data: {
        ReadAtUtc: s.readAtUtc,
        DismissedAtUtc: s.dismissedAtUtc,
        TenantId: tenantId,
      },
    });
  }

  async listForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
    unreadOnly?: boolean;
  }): Promise<NotificationSnapshot[]> {
    const whereBase = {
      TenantId: input.tenantId,
      UserId: input.userId,
      DismissedAtUtc: null,
    };
    const where = input.unreadOnly ? { ...whereBase, ReadAtUtc: null } : whereBase;
    const rows = await this.prisma.notificationEntry.findMany({
      where,
      orderBy: { CreatedAtUtc: 'desc' },
      take: input.take,
      skip: input.skip,
    });
    return rows.map((r) => this.toSnapshot(r));
  }

  async countUnread(tenantId: string, userId: string): Promise<number> {
    return this.prisma.notificationEntry.count({
      where: {
        TenantId: tenantId,
        UserId: userId,
        ReadAtUtc: null,
        DismissedAtUtc: null,
      },
    });
  }

  private toSnapshot(row: {
    Id: string;
    TenantId: string;
    UserId: string;
    Kind: string;
    Priority: string;
    Title: string;
    Body: string;
    MetadataJson: string;
    SourceEventId: string;
    SourceEventType: string;
    CorrelationId: string | null;
    ReadAtUtc: Date | null;
    DismissedAtUtc: Date | null;
    CreatedAtUtc: Date;
  }): NotificationSnapshot {
    return {
      id: row.Id,
      tenantId: row.TenantId,
      userId: row.UserId,
      kind: row.Kind,
      priority: isPriority(row.Priority) ? row.Priority : 'Normal',
      title: row.Title,
      body: row.Body,
      metadata: JSON.parse(row.MetadataJson) as Record<string, unknown>,
      sourceEventId: row.SourceEventId,
      sourceEventType: row.SourceEventType,
      correlationId: row.CorrelationId,
      readAtUtc: row.ReadAtUtc,
      dismissedAtUtc: row.DismissedAtUtc,
      createdAtUtc: row.CreatedAtUtc,
    };
  }
}
