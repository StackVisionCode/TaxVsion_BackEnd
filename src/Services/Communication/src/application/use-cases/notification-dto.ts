import type { NotificationSnapshot } from '../../domain/notifications/notification.js';
import type { NotificationDto } from '../../contracts/socket/notification-socket-events.js';

export function toNotificationDto(snapshot: NotificationSnapshot): NotificationDto {
  return {
    id: snapshot.id,
    kind: snapshot.kind,
    priority: snapshot.priority,
    title: snapshot.title,
    body: snapshot.body,
    metadata: snapshot.metadata,
    category: snapshot.category ?? null,
    actorUserId: snapshot.actorUserId ?? null,
    actorDisplayName: snapshot.actorDisplayName ?? null,
    actorType: snapshot.actorType ?? null,
    resourceType: snapshot.resourceType ?? null,
    resourceId: snapshot.resourceId ?? null,
    resourceContext: snapshot.resourceContext ?? {},
    titleKey: snapshot.titleKey ?? null,
    bodyKey: snapshot.bodyKey ?? null,
    params: snapshot.params ?? {},
    action: snapshot.action ?? null,
    groupKey: snapshot.groupKey ?? null,
    occurredAtUtc: snapshot.occurredAtUtc?.toISOString() ?? null,
    createdAtUtc: snapshot.createdAtUtc.toISOString(),
    readAtUtc: snapshot.readAtUtc?.toISOString() ?? null,
  };
}
