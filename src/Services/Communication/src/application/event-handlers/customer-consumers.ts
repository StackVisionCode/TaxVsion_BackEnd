import { randomUUID } from 'node:crypto';
import { pushNotification } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';

/**
 * Cierra TODO explicito en src/Services/Customer/DependencyInjection.cs:46 y
 * README §25.15: bulk import completado -> notificacion push al usuario que lo
 * lanzo.
 */
export function bindCustomerConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): void {
  register('customer.bulk_imported.v1', async (env) => {
    const createdBy =
      getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
    const totalRows = getNumber(env.payload, 'totalRows') ?? getNumber(env.payload, 'TotalRows') ?? 0;
    const successCount =
      getNumber(env.payload, 'successCount') ?? getNumber(env.payload, 'SuccessCount') ?? 0;
    const failedCount =
      getNumber(env.payload, 'failedCount') ?? getNumber(env.payload, 'FailedCount') ?? 0;
    const importJobId =
      getString(env.payload, 'importJobId') ?? getString(env.payload, 'ImportJobId') ?? '';
    if (!createdBy) return;

    const result = await pushNotification(
      {
        tenantId: env.tenantId,
        userId: createdBy,
        kind: 'customer.bulk_import_completed',
        priority: failedCount > 0 ? 'High' : 'Normal',
        title: 'Importacion masiva completada',
        body: `${successCount}/${totalRows} clientes importados${failedCount > 0 ? `, ${failedCount} con errores` : ''}.`,
        metadata: { importJobId, totalRows, successCount, failedCount },
        sourceEventId: env.eventId,
        sourceEventType: env.eventType,
        correlationId: env.correlationId ?? null,
      },
      deps,
    );
    if (result.isSuccess && result.value.created && result.value.notification) {
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId: createdBy,
        event: NotificationSocketEvents.Received,
        envelope: {
          eventId: randomUUID(),
          correlationId: env.correlationId ?? '',
          emittedAtUtc: new Date().toISOString(),
          payload: result.value.notification,
        },
      });
      deps.emitter.emitToUser({
        tenantId: env.tenantId,
        userId: createdBy,
        event: NotificationSocketEvents.UnreadCountChanged,
        envelope: {
          eventId: randomUUID(),
          correlationId: env.correlationId ?? '',
          emittedAtUtc: new Date().toISOString(),
          payload: { count: result.value.unreadCount },
        },
      });
    }
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];
  if (typeof value === 'number') return value;
  if (typeof value === 'string') {
    const parsed = Number.parseInt(value, 10);
    if (Number.isFinite(parsed)) return parsed;
  }
  return undefined;
}
