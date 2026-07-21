import type { PrismaClient } from '@prisma/client';
import type {
  NotificationActionMappingRepository,
  NotificationActionMappingSnapshot,
  NotificationActionType,
} from '../../application/ports/notification-action-mapping-repository.js';

export class PrismaNotificationActionMappingRepository implements NotificationActionMappingRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findByEventKeyAndAudienceRole(
    eventKey: string,
    audienceRole: string,
  ): Promise<NotificationActionMappingSnapshot | null> {
    const row = await this.prisma.notificationActionMapping.findUnique({
      where: { EventKey_AudienceRole: { EventKey: eventKey, AudienceRole: audienceRole } },
    });
    return row ? toSnapshot(row) : null;
  }

  async list(): Promise<readonly NotificationActionMappingSnapshot[]> {
    const rows = await this.prisma.notificationActionMapping.findMany({
      orderBy: [{ EventKey: 'asc' }, { AudienceRole: 'asc' }],
    });
    return rows.map(toSnapshot);
  }

  async create(input: {
    eventKey: string;
    audienceRole: string;
    actionType: NotificationActionType;
    urlTemplate: string | null;
  }): Promise<NotificationActionMappingSnapshot> {
    const row = await this.prisma.notificationActionMapping.create({
      data: {
        EventKey: input.eventKey,
        AudienceRole: input.audienceRole,
        ActionType: input.actionType,
        UrlTemplate: input.urlTemplate,
      },
    });
    return toSnapshot(row);
  }

  async update(
    id: string,
    input: { actionType: NotificationActionType; urlTemplate: string | null },
  ): Promise<NotificationActionMappingSnapshot | null> {
    try {
      const row = await this.prisma.notificationActionMapping.update({
        where: { Id: id },
        data: { ActionType: input.actionType, UrlTemplate: input.urlTemplate },
      });
      return toSnapshot(row);
    } catch {
      return null;
    }
  }
}

function toSnapshot(row: {
  Id: string;
  EventKey: string;
  AudienceRole: string;
  ActionType: string;
  UrlTemplate: string | null;
}): NotificationActionMappingSnapshot {
  return {
    id: row.Id,
    eventKey: row.EventKey,
    audienceRole: row.AudienceRole,
    actionType: row.ActionType as NotificationActionType,
    urlTemplate: row.UrlTemplate,
  };
}
