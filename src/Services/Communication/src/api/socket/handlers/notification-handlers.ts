import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import {
  MarkNotificationReadPayloadSchema,
  DismissNotificationPayloadSchema,
  NotificationSocketEvents,
} from '../../../contracts/socket/notification-socket-events.js';
import { markNotificationRead, dismissNotification } from '../../../application/use-cases/notification-queries.js';
import type { SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';

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
      envelope: envelope({ notificationId: result.value.notificationId }),
    });
    emitter.emitToUser({
      tenantId,
      userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: envelope({ count: result.value.unreadCount }),
    });
  });

  socket.on(NotificationSocketEvents.Dismiss, async (...args: unknown[]) => {
    const parsed = DismissNotificationPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
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
      envelope: envelope({ count: result.value.unreadCount }),
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
