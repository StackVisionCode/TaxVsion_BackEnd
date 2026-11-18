import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { CustomerDirectoryRepository } from '../ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { emitUserProfileChanged, readOptionalGuid } from './user-profile-realtime.js';
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
 * Mantiene al dia CustomerDirectoryEntry, la proyeccion que usa
 * create-meeting-invitations.ts para autocompletar customers por nombre/email sin
 * round-trip HTTP a Customer. El aviso de import terminado o fallido lo crea Notification.
 */
export function bindCustomerConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    emitter: RealtimeEmitter;
    customerDirectory: CustomerDirectoryRepository;
    customerPreparerAssignments: CustomerPreparerAssignmentRepository;
    customerAssignments: CustomerAssignmentProjectionRepository;
    /**
     * F5 avatares — para anunciar `user.profile.changed` del usuario de portal enlazado cuando cambia la
     * foto del cliente. Opcionales: sin ellos solo se proyecta (y sale `customer.changed` como siempre).
     */
    customerPortalAccounts?: CustomerPortalAccountRepository;
    userDirectory?: UserDirectoryRepository;
  },
): void {
  /** La foto del cliente es la de su cuenta de portal: si tiene una activa, avisar por su userId. */
  async function announcePortalAvatarChanged(
    env: IncomingEnvelope,
    customerId: string,
    customerDisplayName: string,
    avatarFileId: string | null,
  ): Promise<void> {
    if (!deps.customerPortalAccounts) return;
    const account = await deps.customerPortalAccounts.findActiveByCustomerId(customerId);
    if (!account) return;
    // El chat muestra al cliente con su nombre de Auth; si no hay fila, el de la ficha.
    const entry = deps.userDirectory ? await deps.userDirectory.findByUserId(account.userId) : null;
    emitUserProfileChanged(
      deps.emitter,
      env.tenantId,
      { userId: account.userId, displayName: entry?.displayName ?? customerDisplayName, avatarFileId },
      env.correlationId,
    );
  }

  // Fase Backend 10 — CustomerDirectoryEntry. `Kind` y los otros atributos del
  // cliente no se proyectan aqui: solo lo que un autocomplete de invitacion
  // necesita (nombre + email), igual criterio que UserDirectoryEntry para empleados.
  // F-E: el email es opcional (D-3); sin email el cliente igual entra, por su nombre.
  register('customer.created.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const displayName = getString(env.payload, 'displayName') ?? getString(env.payload, 'DisplayName');
    const email = getString(env.payload, 'primaryEmail') ?? getString(env.payload, 'PrimaryEmail') ?? null;
    if (!customerId || !displayName) return;
    const avatarFileId = readOptionalGuid(env.payload, 'avatarFileId', 'AvatarFileId');
    await deps.customerDirectory.upsert({
      customerId,
      tenantId: env.tenantId,
      displayName,
      email,
      isActive: true,
      ...(avatarFileId !== undefined ? { avatarFileId } : {}),
      occurredAtUtc: envOccurredAt(env),
    });
    emitCustomerChanged(env, deps.emitter, customerId, 'created');
  });

  register('customer.updated.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    const displayName = getString(env.payload, 'displayName') ?? getString(env.payload, 'DisplayName');
    const email = getString(env.payload, 'primaryEmail') ?? getString(env.payload, 'PrimaryEmail') ?? null;
    if (!customerId || !displayName) return;
    // Clave ausente = publicador viejo (antes de F3): no tocar la foto guardada.
    const avatarFileId = readOptionalGuid(env.payload, 'avatarFileId', 'AvatarFileId');
    // E-3: el estado del aviso deja bien la fila que se crea tarde; sin estado (mensaje viejo) no se toca.
    const status = getString(env.payload, 'status') ?? getString(env.payload, 'Status');
    const existing = await deps.customerDirectory.findByCustomerId(env.tenantId, customerId);
    await deps.customerDirectory.upsert({
      customerId,
      tenantId: env.tenantId,
      displayName,
      email,
      ...(status !== undefined ? { isActive: status === 'Active' } : {}),
      ...(avatarFileId !== undefined ? { avatarFileId } : {}),
      occurredAtUtc: envOccurredAt(env),
    });
    emitCustomerChanged(env, deps.emitter, customerId, 'updated');
    if (avatarFileId !== undefined && avatarFileId !== (existing?.avatarFileId ?? null)) {
      await announcePortalAvatarChanged(env, customerId, displayName, avatarFileId);
    }
  });

  // Bloque guardado del import: la ficha de cada cliente creado o actualizado, sin esperar la reconciliacion.
  register('customer.import_chunk_committed.v1', async (env) => {
    if (!env.tenantId) return;
    const cards = env.payload['customers'] ?? env.payload['Customers'];
    if (!Array.isArray(cards)) return;
    for (const card of cards) {
      if (typeof card !== 'object' || card === null) continue;
      const fields = card as Record<string, unknown>;
      const customerId = getString(fields, 'customerId') ?? getString(fields, 'CustomerId');
      const displayName = getString(fields, 'displayName') ?? getString(fields, 'DisplayName');
      // Sin email también se proyecta, igual que el alta (D-3).
      const email = getString(fields, 'primaryEmail') ?? getString(fields, 'PrimaryEmail') ?? null;
      if (!customerId || !displayName) continue;
      const status = getString(fields, 'status') ?? getString(fields, 'Status') ?? 'Active';
      const avatarFileId = readOptionalGuid(fields, 'avatarFileId', 'AvatarFileId');
      await deps.customerDirectory.upsert({
        customerId,
        tenantId: env.tenantId,
        displayName,
        email,
        isActive: status === 'Active',
        ...(avatarFileId !== undefined ? { avatarFileId } : {}),
        occurredAtUtc: envOccurredAt(env),
      });
      // Solo los existentes estan en el cache del CRM: los nuevos no generan una rafaga de avisos.
      const isNew = fields['isNew'] ?? fields['IsNew'];
      if (isNew === false) emitCustomerChanged(env, deps.emitter, customerId, 'updated');
    }
  });

  // Estado en el directorio: cada aviso lleva su fecha (occurredOn) y los mas viejos se descartan.
  register('customer.deactivated.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerDirectory.markInactive(customerId, envOccurredAt(env));
    emitCustomerChanged(env, deps.emitter, customerId, 'deactivated');
  });

  // Restaurado (Archived→Active) o activado (Inactive→Active): sin esto seguia inactivo hasta la reconciliacion.
  async function markCustomerActive(
    env: IncomingEnvelope,
    changeType: 'reactivated' | 'activated',
  ): Promise<void> {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    await deps.customerDirectory.markActive(customerId, envOccurredAt(env));
    emitCustomerChanged(env, deps.emitter, customerId, changeType);
  }
  register('customer.reactivated.v1', (env) => markCustomerActive(env, 'reactivated'));
  register('customer.activated.v1', (env) => markCustomerActive(env, 'activated'));

  // 2026-08-06 (auditoria de proyecciones de customer): antes solo se escuchaba
  // customer.deactivated.v1, asi que un cliente ARCHIVADO seguia con isActive=true
  // en el directorio y aparecia en el autocomplete de invitaciones. Archive se
  // trata igual que deactivate para esta proyeccion (mismo criterio que las 3
  // proyecciones .NET: Signature/Correspondence/Notes).
  register('customer.archived.v1', async (env) => {
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (!customerId) return;
    // F5: archivar borra la foto en Customer y el evento no la trae; la proyeccion la limpia igual.
    const existing = await deps.customerDirectory.findByCustomerId(env.tenantId, customerId);
    await deps.customerDirectory.markInactive(customerId, envOccurredAt(env));
    await deps.customerDirectory.clearAvatar(customerId);
    emitCustomerChanged(env, deps.emitter, customerId, 'archived');
    if (existing?.avatarFileId) {
      await announcePortalAvatarChanged(env, customerId, existing.displayName, null);
    }
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

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo terminó el
 * import), distinta de `CreatedAtUtc` del repo que es la hora de recepción.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw =
    getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn') ?? env.occurredOnUtc;
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}
