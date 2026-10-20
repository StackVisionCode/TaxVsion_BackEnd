import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { CommunicationIoServer, CommunicationSocket } from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import {
  MarkNotificationReadPayloadSchema,
  DismissNotificationPayloadSchema,
  NotificationSocketEvents,
} from '../../../contracts/socket/notification-socket-events.js';
import {
  markNotificationRead,
  dismissNotification,
} from '../../../application/use-cases/notification-queries.js';
import type { SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';
import { checkPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';

export function registerNotificationHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => wireNotifSocket(socket, container, emitter));
}

function wireNotifSocket(
  socket: CommunicationSocket,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
): void {
  const principal = socket.data.principal;
  if (!principal) return;
  const { tenantId, userId } = principal;

  socket.on(NotificationSocketEvents.MarkRead, async (...args: unknown[]) => {
    const parsed = MarkNotificationReadPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const permission = await checkPermission(
      principal,
      CommunicationPermissions.NotificationRead,
      container.userPermissions,
    );
    if (!permission.allowed) {
      logger.debug({ code: permission.code }, 'mark_read denied');
      return;
    }
    const result = await markNotificationRead(
      { tenantId, userId, notificationId: parsed.data.notificationId },
      container,
    );
    if (!result.isSuccess) {
      logger.debug({ err: result.error }, 'mark_read rejected');
      return;
    }
    emitter.emitToUser({
      tenantId,
      userId,
      event: NotificationSocketEvents.ReadConfirmed,
      envelope: envelope({ notificationId: result.value.notificationId, asOfUtc: result.value.asOfUtc }),
    });
    emitter.emitToUser({
      tenantId,
      userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: envelope({ count: result.value.unreadCount, asOfUtc: result.value.asOfUtc }),
    });
  });

  socket.on(NotificationSocketEvents.Dismiss, async (...args: unknown[]) => {
    const parsed = DismissNotificationPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const permission = await checkPermission(
      principal,
      CommunicationPermissions.NotificationRead,
      container.userPermissions,
    );
    if (!permission.allowed) {
      logger.debug({ code: permission.code }, 'dismiss_notification denied');
      return;
    }
    const result = await dismissNotification(
      { tenantId, userId, notificationId: parsed.data.notificationId },
      container,
    );
    if (!result.isSuccess) {
      logger.debug({ err: result.error }, 'dismiss rejected');
      return;
    }
    emitter.emitToUser({
      tenantId,
      userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: envelope({ count: result.value.unreadCount, asOfUtc: result.value.asOfUtc }),
    });
  });
}

function envelope<T>(payload: T): SocketEnvelope<T> {
  return {
    eventId: randomUUID(),
    correlationId: '',
    emittedAtUtc: new Date().toISOString(),
    payload,
  };
}
