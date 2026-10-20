import type { NotificationEntry, PrismaClient } from '@prisma/client';
import { Prisma } from '@prisma/client';
import {
  Notification,
  isPriority,
  isActorType,
  type NotificationSnapshot,
} from '../../domain/notifications/notification.js';
import type { NotificationRepository } from '../../application/ports/notification-repository.js';
import type {
  InAppNotificationPersistenceResult,
  InAppNotificationRepository,
} from '../../application/ports/in-app-notification-repository.js';

export class PrismaNotificationRepository implements NotificationRepository, InAppNotificationRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async createIfMissing(notification: Notification): Promise<boolean> {
    const s = notification.toSnapshot();
    try {
      await this.prisma.notificationEntry.create({
        data: this.toCreateData(s),
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

  async persist(notification: Notification): Promise<InAppNotificationPersistenceResult> {
    const snapshot = notification.toSnapshot();
    if (!snapshot.groupKey) {
      const created = await this.createIfMissing(notification);
      return { status: created ? 'created' : 'duplicate', notification: created ? snapshot : null };
    }
    const groupKey = snapshot.groupKey;

    for (let attempt = 1; attempt <= 3; attempt += 1) {
      try {
        return await this.prisma.$transaction(
          async (tx) => {
            const duplicate = await tx.notificationEntry.findUnique({
              where: {
                TenantId_SourceEventId_UserId: {
                  TenantId: snapshot.tenantId,
                  SourceEventId: snapshot.sourceEventId,
                  UserId: snapshot.userId,
                },
              },
            });
            if (duplicate) return { status: 'duplicate', notification: null };

            const grouped = await tx.notificationEntry.findFirst({
              where: {
                TenantId: snapshot.tenantId,
                UserId: snapshot.userId,
                GroupKey: groupKey,
                ReadAtUtc: null,
                DismissedAtUtc: null,
              },
              orderBy: { CreatedAtUtc: 'desc' },
            });

            if (!grouped) {
              const created = await tx.notificationEntry.create({ data: this.toCreateData(snapshot, true) });
              return { status: 'created', notification: this.toSnapshot(created) };
            }

            const currentParams = grouped.ParamsJson
              ? (JSON.parse(grouped.ParamsJson) as Record<string, string>)
              : {};
            const currentCount = Number.parseInt(currentParams['count'] ?? '1', 10);
            const params = {
              ...currentParams,
              ...(snapshot.params ?? {}),
              count: String(Number.isFinite(currentCount) ? currentCount + 1 : 2),
            };
            const occurredAtUtc = snapshot.occurredAtUtc ?? snapshot.createdAtUtc;
            // Reagrupado: CreatedAtUtc se refresca con la hora del servidor para que el
            // ítem suba al tope ordenado por recencia real, sin heredar la fecha del hecho.
            const regroupedAtUtc = new Date();
            const updated = await tx.notificationEntry.update({
              where: { Id: grouped.Id },
              data: {
                Priority: snapshot.priority,
                Title: snapshot.title,
                Body: snapshot.body,
                MetadataJson: JSON.stringify(snapshot.metadata),
                Category: snapshot.category ?? null,
                ActorUserId: snapshot.actorUserId ?? null,
                ActorDisplayName: snapshot.actorDisplayName ?? null,
                ActorType: snapshot.actorType ?? null,
                ResourceType: snapshot.resourceType ?? null,
                ResourceId: snapshot.resourceId ?? null,
                ResourceContextJson: JSON.stringify(snapshot.resourceContext ?? {}),
                TitleKey: snapshot.titleKey ?? null,
                BodyKey: snapshot.bodyKey ?? null,
                ParamsJson: JSON.stringify(params),
                Action: snapshot.action ?? null,
                OccurredAtUtc: occurredAtUtc,
                CreatedAtUtc: regroupedAtUtc,
                CorrelationId: snapshot.correlationId ?? null,
              },
            });
            return { status: 'updated', notification: this.toSnapshot(updated) };
          },
          { isolationLevel: Prisma.TransactionIsolationLevel.Serializable },
        );
      } catch (error) {
        if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2034' && attempt < 3)
          continue;
        throw error;
      }
    }

    throw new Error('Unable to persist grouped notification.');
  }

  async findById(tenantId: string, id: string, userId: string): Promise<Notification | null> {
    const row = await this.prisma.notificationEntry.findFirst({
      where: { Id: id, TenantId: tenantId, UserId: userId },
    });
    return row ? Notification.rehydrate(this.toSnapshot(row)) : null;
  }

  async markRead(tenantId: string, userId: string, id: string, readAtUtc: Date): Promise<boolean> {
    const updated = await this.prisma.notificationEntry.updateMany({
      where: { Id: id, TenantId: tenantId, UserId: userId, ReadAtUtc: null },
      data: { ReadAtUtc: readAtUtc },
    });
    if (updated.count > 0) return true;
    return (
      (await this.prisma.notificationEntry.count({ where: { Id: id, TenantId: tenantId, UserId: userId } })) >
      0
    );
  }

  async markUnread(tenantId: string, userId: string, id: string): Promise<boolean> {
    const updated = await this.prisma.notificationEntry.updateMany({
      where: { Id: id, TenantId: tenantId, UserId: userId, ReadAtUtc: { not: null } },
      data: { ReadAtUtc: null },
    });
    if (updated.count > 0) return true;
    return (
      (await this.prisma.notificationEntry.count({ where: { Id: id, TenantId: tenantId, UserId: userId } })) >
      0
    );
  }

  async dismiss(tenantId: string, userId: string, id: string, dismissedAtUtc: Date): Promise<boolean> {
    const updated = await this.prisma.notificationEntry.updateMany({
      where: { Id: id, TenantId: tenantId, UserId: userId, DismissedAtUtc: null },
      data: { DismissedAtUtc: dismissedAtUtc },
    });
    if (updated.count > 0) return true;
    return (
      (await this.prisma.notificationEntry.count({ where: { Id: id, TenantId: tenantId, UserId: userId } })) >
      0
    );
  }

  async markAllRead(input: {
    tenantId: string;
    userId: string;
    cutoffUtc: Date;
    readAtUtc: Date;
  }): Promise<{ markedCount: number; unreadCount: number }> {
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.notificationEntry.updateMany({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          ReadAtUtc: null,
          DismissedAtUtc: null,
          CreatedAtUtc: { lte: input.cutoffUtc },
        },
        data: { ReadAtUtc: input.readAtUtc },
      });
      const unreadCount = await tx.notificationEntry.count({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          ReadAtUtc: null,
          DismissedAtUtc: null,
        },
      });
      return { markedCount: updated.count, unreadCount };
    });
  }

  async markManyRead(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
    readAtUtc: Date;
  }): Promise<{ markedCount: number; unreadCount: number }> {
    if (input.ids.length === 0) {
      const unreadCount = await this.countUnread(input.tenantId, input.userId);
      return { markedCount: 0, unreadCount };
    }
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.notificationEntry.updateMany({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          Id: { in: [...input.ids] },
          ReadAtUtc: null,
          DismissedAtUtc: null,
        },
        data: { ReadAtUtc: input.readAtUtc },
      });
      const unreadCount = await tx.notificationEntry.count({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          ReadAtUtc: null,
          DismissedAtUtc: null,
        },
      });
      return { markedCount: updated.count, unreadCount };
    });
  }

  async markManyUnread(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
  }): Promise<{ unmarkedCount: number; unreadCount: number }> {
    if (input.ids.length === 0) {
      const unreadCount = await this.countUnread(input.tenantId, input.userId);
      return { unmarkedCount: 0, unreadCount };
    }
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.notificationEntry.updateMany({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          Id: { in: [...input.ids] },
          ReadAtUtc: { not: null },
          DismissedAtUtc: null,
        },
        data: { ReadAtUtc: null },
      });
      const unreadCount = await tx.notificationEntry.count({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          ReadAtUtc: null,
          DismissedAtUtc: null,
        },
      });
      return { unmarkedCount: updated.count, unreadCount };
    });
  }

  async dismissMany(input: {
    tenantId: string;
    userId: string;
    ids: readonly string[];
    dismissedAtUtc: Date;
  }): Promise<{ dismissedCount: number; unreadCount: number }> {
    if (input.ids.length === 0) {
      const unreadCount = await this.countUnread(input.tenantId, input.userId);
      return { dismissedCount: 0, unreadCount };
    }
    return this.prisma.$transaction(async (tx) => {
      const updated = await tx.notificationEntry.updateMany({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          Id: { in: [...input.ids] },
          DismissedAtUtc: null,
        },
        data: { DismissedAtUtc: input.dismissedAtUtc },
      });
      const unreadCount = await tx.notificationEntry.count({
        where: {
          TenantId: input.tenantId,
          UserId: input.userId,
          ReadAtUtc: null,
          DismissedAtUtc: null,
        },
      });
      return { dismissedCount: updated.count, unreadCount };
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

  async countForUser(input: {
    tenantId: string;
    userId: string;
    unreadOnly?: boolean;
  }): Promise<number> {
    const whereBase = {
      TenantId: input.tenantId,
      UserId: input.userId,
      DismissedAtUtc: null,
    };
    const where = input.unreadOnly ? { ...whereBase, ReadAtUtc: null } : whereBase;
    return this.prisma.notificationEntry.count({ where });
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

  async listSinceForUser(input: {
    tenantId: string;
    userId: string;
    sinceUtc: Date;
    limit: number;
  }): Promise<NotificationSnapshot[]> {
    const rows = await this.prisma.notificationEntry.findMany({
      where: {
        TenantId: input.tenantId,
        UserId: input.userId,
        DismissedAtUtc: null,
        CreatedAtUtc: { gt: input.sinceUtc },
      },
      // ASC por CreatedAtUtc + Id de tiebreaker: la combinación es estable
      // bajo re-ejecución y permite al cliente usar el último `createdAtUtc`
      // como próximo cursor sin perder filas con timestamp idéntico.
      orderBy: [{ CreatedAtUtc: 'asc' }, { Id: 'asc' }],
      take: input.limit,
    });
    return rows.map((r) => this.toSnapshot(r));
  }

  private toCreateData(snapshot: NotificationSnapshot, initializeGroupCount = false) {
    const params = initializeGroupCount
      ? { ...snapshot.params, count: snapshot.params?.['count'] ?? '1' }
      : snapshot.params;
    return {
      Id: snapshot.id,
      TenantId: snapshot.tenantId,
      UserId: snapshot.userId,
      Kind: snapshot.kind,
      Priority: snapshot.priority,
      Title: snapshot.title,
      Body: snapshot.body,
      MetadataJson: JSON.stringify(snapshot.metadata),
      Category: snapshot.category ?? null,
      ActorUserId: snapshot.actorUserId ?? null,
      ActorDisplayName: snapshot.actorDisplayName ?? null,
      ActorType: snapshot.actorType ?? null,
      ResourceType: snapshot.resourceType ?? null,
      ResourceId: snapshot.resourceId ?? null,
      ResourceContextJson: JSON.stringify(snapshot.resourceContext ?? {}),
      TitleKey: snapshot.titleKey ?? null,
      BodyKey: snapshot.bodyKey ?? null,
      ParamsJson: JSON.stringify(params ?? {}),
      Action: snapshot.action ?? null,
      GroupKey: snapshot.groupKey ?? null,
      OccurredAtUtc: snapshot.occurredAtUtc ?? null,
      SourceEventId: snapshot.sourceEventId,
      SourceEventType: snapshot.sourceEventType,
      CorrelationId: snapshot.correlationId ?? null,
      CreatedAtUtc: snapshot.createdAtUtc,
    };
  }

  private toSnapshot(row: NotificationEntry): NotificationSnapshot {
    return {
      id: row.Id,
      tenantId: row.TenantId,
      userId: row.UserId,
      kind: row.Kind,
      priority: isPriority(row.Priority) ? row.Priority : 'Normal',
      title: row.Title,
      body: row.Body,
      metadata: JSON.parse(row.MetadataJson) as Record<string, unknown>,
      category: row.Category,
      actorUserId: row.ActorUserId,
      actorDisplayName: row.ActorDisplayName,
      // isActorType filtra valores viejos/basura por si una fila legacy tiene un enum fuera del set.
      actorType: row.ActorType && isActorType(row.ActorType) ? row.ActorType : null,
      resourceType: row.ResourceType,
      resourceId: row.ResourceId,
      resourceContext: row.ResourceContextJson
        ? (JSON.parse(row.ResourceContextJson) as Record<string, string>)
        : {},
      titleKey: row.TitleKey,
      bodyKey: row.BodyKey,
      params: row.ParamsJson ? (JSON.parse(row.ParamsJson) as Record<string, string>) : {},
      action: row.Action,
      groupKey: row.GroupKey,
      occurredAtUtc: row.OccurredAtUtc,
      sourceEventId: row.SourceEventId,
      sourceEventType: row.SourceEventType,
      correlationId: row.CorrelationId,
      readAtUtc: row.ReadAtUtc,
      dismissedAtUtc: row.DismissedAtUtc,
      createdAtUtc: row.CreatedAtUtc,
    };
  }
}
