import type { Call } from '../../domain/calls/call.js';
import type { Meeting } from '../../domain/meetings/meeting.js';
import { isStaffActor } from '../../domain/shared/permissions.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { seesAllCustomers } from '../use-cases/customer-visibility.js';
import type { ClientCandidate } from './file-source-declarer.js';

/** La llamada o la reunión de una grabación. */
export type RecordingSubject =
  | { readonly scope: 'Meeting'; readonly meeting: Meeting }
  | { readonly scope: 'Call'; readonly call: Call };

/** Lo que hace falta para saber si el personal ve a los clientes de una llamada. */
export interface RecordingClientAccessDeps {
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly customerAssignments: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'>;
  /** Visibilidad por asignación activa en el despliegue. */
  readonly assignmentVisibilityEnabled: boolean;
}

export interface RecordingViewerIdentity {
  readonly tenantId: string;
  readonly userId: string;
  readonly actorType: string;
}

/** Anfitrión o participante conocido de la reunión; una de las dos partes de la llamada. */
export function participatedInRecording(subject: RecordingSubject, userId: string): boolean {
  return subject.scope === 'Meeting'
    ? subject.meeting.hostUserId === userId || subject.meeting.isKnownParticipant(userId)
    : subject.call.isParticipant(userId);
}

/** Participantes de la llamada o la reunión, en la forma con que se elige el cliente relacionado. */
export function recordingClientCandidates(subject: RecordingSubject): ClientCandidate[] {
  if (subject.scope === 'Meeting') {
    return (subject.meeting.toSnapshot().participants ?? []).map((p) => ({
      actorType: p.actorType,
      customerId: p.customerId,
      isRemoved: false,
    }));
  }
  return (subject.call.toSnapshot().participants ?? []).map((p) => ({
    actorType: p.customerId ? 'CustomerPortal' : 'TenantEmployee',
    customerId: p.customerId,
    isRemoved: false,
  }));
}

/** Clientes de la llamada (participantes con cliente). */
export function callCustomerIds(call: Call): string[] {
  return (call.toSnapshot().participants ?? []).map((p) => p.customerId).filter((id): id is string => !!id);
}

/**
 * ¿Ve el personal a los clientes de la llamada? Sin clientes, o sin visibilidad por asignación, sí; si no, quien ve a
 * todos o tiene asignado alguno de ellos.
 */
export async function seesRecordingClients(
  viewer: RecordingViewerIdentity,
  customerIds: readonly string[],
  deps: RecordingClientAccessDeps,
): Promise<boolean> {
  if (customerIds.length === 0 || !deps.assignmentVisibilityEnabled) return true;
  if (await seesAllCustomers(viewer, deps.userPermissions)) return true;
  const assigned = await Promise.all(
    customerIds.map((id) => deps.customerAssignments.isAssigned(viewer.tenantId, id, viewer.userId)),
  );
  return assigned.some(Boolean);
}

/**
 * Regla de la biblioteca (E-1, la de las pantallas): quien participó; y en una llamada con cliente, también el
 * personal con `communication.call.record` que ve a ese cliente. Nunca reuniones ni llamadas internas ajenas. El
 * permiso de entrada del tipo y el estado de la sesión los comprueba quien llama.
 */
export async function canSeeRecording(
  viewer: RecordingViewerIdentity & { readonly holdsCallRecordPermission: boolean },
  subject: RecordingSubject,
  deps: RecordingClientAccessDeps,
): Promise<boolean> {
  if (participatedInRecording(subject, viewer.userId)) return true;
  if (subject.scope === 'Meeting') return false;
  if (!isStaffActor(viewer.actorType) || !viewer.holdsCallRecordPermission) return false;
  const customerIds = callCustomerIds(subject.call);
  return customerIds.length > 0 && (await seesRecordingClients(viewer, customerIds, deps));
}
