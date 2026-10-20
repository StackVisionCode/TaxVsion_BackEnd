import { randomUUID } from 'node:crypto';
import { pushAndEmitNotification } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { CustomerDirectoryRepository } from '../ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import {
  CustomerSocketEvents,
  type CustomerChangedDto,
} from '../../contracts/socket/customer-socket-events.js';

/** Avisa al personal del tenant que un cliente cambió, para que el cache de clientes del CRM lo invalide (backlog 5.1). */
function emitCustomerChanged(
  env: IncomingEnvelope,
  emitter: RealtimeEmitter,
  customerId: string,
  changeType: CustomerChangedDto['changeType'],
): void {
  emitter.emitToTenantStaff<CustomerChangedDto>({
    tenantId: env.tenantId,
    event: CustomerSocketEvents.Changed,
    envelope: {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: new Date().toISOString(),
      payload: { customerId, changeType },
    },
  });
}

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
    customerAssignments: CustomerAssignmentProjectionRepository;
    inAppSource: 'Communication' | 'Notification';
  },
): void {
  register('customer.bulk_imported.v1', async (env) => {
    if (deps.inAppSource === 'Notification') return;
    const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
    const totalRows = getNumber(env.payload, 'totalRows') ?? getNumber(env.payload, 'TotalRows') ?? 0;
    const successCount =
      getNumber(env.payload, 'successCount') ?? getNumber(env.payload, 'SuccessCount') ?? 0;
    const failedCount = getNumber(env.payload, 'failedCount') ?? getNumber(env.payload, 'FailedCount') ?? 0;
    const importJobId = getString(env.payload, 'importJobId') ?? getString(env.payload, 'ImportJobId') ?? '';
    if (!createdBy) return;

    await pushAndEmitNotification(
      env,
      {
        userId: createdBy,
        kind: 'customer.bulk_imported',
        priority: failedCount > 0 ? 'High' : 'Normal',
        title: 'Client import completed',
        body: `${successCount}/${totalRows} clients imported${failedCount > 0 ? `, ${failedCount} with errors` : ''}.`,
        metadata: { importJobId, totalRows, successCount, failedCount },
        category: 'Clients',
        actorUserId: createdBy,
        // El evento se auto-dirige al usuario que lanzó el import; actúa como "tú lo hiciste".
        // ActorType=TenantEmployee es el default razonable (los imports son de staff del tenant);
        // si fuera un admin, el front lo diferencia por el matching actorUserId == currentUserId.
        actorType: 'TenantEmployee',
        resourceType: 'import_job',
        resourceId: importJobId || null,
        action: 'review',
        occurredAtUtc: envOccurredAt(env),
      },
      deps,
    );
  });

  // Fase 1B — evento hermano de customer.bulk_imported.v1: cubre el camino de
  // Failed (archivo vacio, excede filas, virus, bloqueado por politica, crash
  // del worker), que antes no publicaba nada — el empleado nunca se enteraba.
  register('customer.bulk_import_failed.v1', async (env) => {
    if (deps.inAppSource === 'Notification') return;
    const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
    const importJobId = getString(env.payload, 'importJobId') ?? getString(env.payload, 'ImportJobId') ?? '';
    const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason') ?? '';
    if (!createdBy) return;

    await pushAndEmitNotification(
      env,
      {
        userId: createdBy,
        kind: 'customer.import_failed',
        priority: 'High',
        title: 'Client import failed',
        body: reason ? `The import could not complete: ${reason}` : 'The import could not complete.',
        metadata: { importJobId, reason },
        category: 'Clients',
        actorUserId: createdBy,
        actorType: 'TenantEmployee',
        resourceType: 'import_job',
        resourceId: importJobId || null,
        action: 'review',
        occurredAtUtc: envOccurredAt(env),
      },
      deps,
    );
  });

  // Fase Backend 10 — CustomerDirectoryEntry. `Kind` y los otros atributos del
  // cliente no se proyectan aqui: solo lo que un autocomplete de invitacion
  // necesita (nombre + email), igual criterio que UserDirectoryEntry para empleados.
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
    emitCustomerChanged(env, deps.emitter, customerId, 'created');
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
    emitCustomerChanged(env, deps.emitter, customerId, 'updated');
  });

  register('customer.deactivated.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerDirectory.markInactive(customerId);
    emitCustomerChanged(env, deps.emitter, customerId, 'deactivated');
  });

  // 2026-08-06 (auditoria de proyecciones de customer): antes solo se escuchaba
  // customer.deactivated.v1, asi que un cliente ARCHIVADO seguia con isActive=true
  // en el directorio y aparecia en el autocomplete de invitaciones. Archive se
  // trata igual que deactivate para esta proyeccion (mismo criterio que las 3
  // proyecciones .NET: Signature/Correspondence/Notes).
  register('customer.archived.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerDirectory.markInactive(customerId);
    emitCustomerChanged(env, deps.emitter, customerId, 'archived');
  });

  // Fase B2 (chat tipado) — mantiene al dia CustomerPreparerAssignment, la
  // proyeccion que start-direct-conversation.ts usa para calcular
  // isPrimaryPreparer sin round-trip HTTP a Customer, mismo criterio que
  // CustomerDirectoryEntry arriba.
  register('customer.preparer_assigned.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const preparerUserId =
      getString(env.payload, 'preparerUserId') ?? getString(env.payload, 'PreparerUserId');
    if (!customerId || !preparerUserId) return;
    await deps.customerPreparerAssignments.assign({ customerId, tenantId: env.tenantId, preparerUserId });
  });

  register('customer.preparer_unassigned.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerPreparerAssignments.unassign(customerId);
  });

  // P2.5 — snapshot M:N del set COMPLETO de asignados (event-carried state transfer). Reemplaza el set
  // local del customer, version-guarded (solo aplica snapshots mas nuevos): descarta reordenados/duplicados.
  // Es la fuente del gate restrictCustomerChatToAssignedPreparer, que con M:N permite a CUALQUIER staff
  // asignado (no solo al primary) chatear con el customer. Coexiste con la proyeccion 1:1 (isPrimaryPreparer).
  register('customer.assignments_changed.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const versionRaw = getString(env.payload, 'version') ?? getString(env.payload, 'Version');
    if (!customerId || !versionRaw) return;
    const version = new Date(versionRaw);
    if (Number.isNaN(version.getTime())) return;

    const applied = await deps.customerAssignments.getVersion(env.tenantId, customerId);
    if (applied && version <= applied) return; // snapshot igual o mas viejo ya aplicado -> ignorar

    const assigneeUserIds =
      getStringArray(env.payload, 'assigneeUserIds') ?? getStringArray(env.payload, 'AssigneeUserIds') ?? [];
    await deps.customerAssignments.replace(env.tenantId, customerId, assigneeUserIds, version);
  });
}

function getStringArray(source: Record<string, unknown>, key: string): string[] | undefined {
  const value = source[key];
  if (!Array.isArray(value)) return undefined;
  return value.filter((item): item is string => typeof item === 'string');
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

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo terminó el
 * import), distinta de `CreatedAtUtc` del repo que es la hora de recepción.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw = getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn');
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}
