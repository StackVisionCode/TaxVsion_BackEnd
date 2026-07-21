import { randomUUID } from 'node:crypto';
import { pushNotification } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { CustomerDirectoryRepository } from '../ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';

/**
 * Cierra TODO explicito en src/Services/Customer/DependencyInjection.cs:46 y
 * README §25.15: bulk import completado -> notificacion push al usuario que lo
 * lanzo. Ademas (Fase Backend 10) mantiene al dia CustomerDirectoryEntry, la
 * proyeccion que usa create-meeting-invitations.ts para autocompletar
 * customers por nombre/email sin round-trip HTTP a Customer.
 */
export function bindCustomerConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    notifications: NotificationRepository;
    emitter: RealtimeEmitter;
    customerDirectory: CustomerDirectoryRepository;
    customerPreparerAssignments: CustomerPreparerAssignmentRepository;
  },
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

  // Fase 1B — evento hermano de customer.bulk_imported.v1: cubre el camino de
  // Failed (archivo vacio, excede filas, virus, bloqueado por politica, crash
  // del worker), que antes no publicaba nada — el empleado nunca se enteraba.
  register('customer.bulk_import_failed.v1', async (env) => {
    const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
    const importJobId = getString(env.payload, 'importJobId') ?? getString(env.payload, 'ImportJobId') ?? '';
    const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason') ?? '';
    if (!createdBy) return;

    const result = await pushNotification(
      {
        tenantId: env.tenantId,
        userId: createdBy,
        kind: 'customer.bulk_import_failed',
        priority: 'High',
        title: 'Importacion masiva fallida',
        body: reason ? `La importacion no pudo completarse: ${reason}` : 'La importacion no pudo completarse.',
        metadata: { importJobId, reason },
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

  // Fase Backend 10 — CustomerDirectoryEntry. `Kind`/`PreferredChannel` etc no
  // se proyectan aqui: solo lo que un autocomplete de invitacion necesita
  // (nombre + email), igual criterio que UserDirectoryEntry para empleados.
  register('customer.created.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const displayName = getString(env.payload, 'displayName') ?? getString(env.payload, 'DisplayName');
    const email = getString(env.payload, 'primaryEmail') ?? getString(env.payload, 'PrimaryEmail');
    if (!customerId || !displayName || !email) return;
    await deps.customerDirectory.upsert({
      customerId,
      tenantId: env.tenantId,
      displayName,
      email,
      isActive: true,
    });
  });

  register('customer.updated.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const displayName = getString(env.payload, 'displayName') ?? getString(env.payload, 'DisplayName');
    const email = getString(env.payload, 'primaryEmail') ?? getString(env.payload, 'PrimaryEmail');
    if (!customerId || !displayName || !email) return;
    const existing = await deps.customerDirectory.findByCustomerId(env.tenantId, customerId);
    await deps.customerDirectory.upsert({
      customerId,
      tenantId: env.tenantId,
      displayName,
      email,
      isActive: existing?.isActive ?? true,
    });
  });

  register('customer.deactivated.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerDirectory.markInactive(customerId);
  });

  // Fase B2 (chat tipado) — mantiene al dia CustomerPreparerAssignment, la
  // proyeccion que start-direct-conversation.ts usa para calcular
  // isPrimaryPreparer sin round-trip HTTP a Customer, mismo criterio que
  // CustomerDirectoryEntry arriba.
  register('customer.preparer_assigned.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const preparerUserId = getString(env.payload, 'preparerUserId') ?? getString(env.payload, 'PreparerUserId');
    if (!customerId || !preparerUserId) return;
    await deps.customerPreparerAssignments.assign({ customerId, tenantId: env.tenantId, preparerUserId });
  });

  register('customer.preparer_unassigned.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerPreparerAssignments.unassign(customerId);
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
