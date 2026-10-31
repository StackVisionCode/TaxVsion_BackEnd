import { Result, makeError } from '../../domain/shared/result.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { TenantCommunicationSettingsSnapshot } from '../ports/tenant-settings-provider.js';
import { seesAllCustomers } from './customer-visibility.js';

/**
 * F8b (T-18): política ÚNICA de quién puede entrar a un grupo. La usan crear grupo y agregar miembro, para
 * que las dos puertas apliquen exactamente las mismas reglas:
 *   1. Mismo tenant y cuenta activa (el directorio local manda; nunca se cruza de oficina).
 *   2. Tipo permitido: staff de la oficina o cliente del portal (nunca Guest ni operadores de plataforma).
 *   3. Cliente: cuenta de portal activa; de ahí sale su `CustomerId` (D-58).
 *   4. ABAC: con visibilidad por asignación, quien agrega a un cliente debe verlo (asignado o "ve todos").
 *   5. D-20: un solo cliente por grupo salvo que la oficina lo permita (se verían entre sí).
 *   6. Grupo solo de empleados = chat interno: respeta `employeeToEmployeeChatEnabled` igual que Direct.
 */
export interface GroupCandidate {
  readonly userId: string;
}

export interface AdmittedGroupMember {
  readonly userId: string;
  readonly displayName: string;
  readonly actorType: string;
  readonly customerId: string | null;
}

export interface GroupMembershipPolicyDeps {
  readonly userDirectory: Pick<UserDirectoryRepository, 'findByUserId'>;
  readonly customerPortalAccounts: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'>;
  readonly customerAssignments: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'>;
  readonly userPermissions: UserPermissionsProjectionRepository;
  /** Mismo flag que el buscador y Direct: con P2 encendido aplica en todos los tenants. */
  readonly assignmentVisibilityEnabled?: boolean;
}

export async function admitGroupMembers(
  input: {
    tenantId: string;
    actor: { userId: string; actorType: string };
    candidates: readonly GroupCandidate[];
    /** Clientes que ya están en el grupo (0 al crear). */
    existingClientCount: number;
    /** Hay staff además del actor ya en el grupo (al agregar); al crear, solo cuentan los candidatos. */
    existingHasNonEmployee?: boolean;
    settings: TenantCommunicationSettingsSnapshot;
  },
  deps: GroupMembershipPolicyDeps,
): Promise<Result<AdmittedGroupMember[]>> {
  const admitted: AdmittedGroupMember[] = [];
  const restrictToAssigned = deps.assignmentVisibilityEnabled || input.settings.restrictCustomerChatToAssignedPreparer;
  let actorSeesAll: boolean | null = null;

  for (const candidate of input.candidates) {
    const entry = await deps.userDirectory.findByUserId(candidate.userId);
    if (!entry) {
      return Result.fail(makeError('Directory.Unresolved', `Member could not be resolved yet: ${candidate.userId}.`));
    }
    if (entry.tenantId !== input.tenantId) {
      return Result.fail(makeError('Chat.Member.NotInTenant', 'Members must belong to this office.'));
    }
    if (!entry.isActive) {
      return Result.fail(makeError('Chat.Member.Inactive', `${entry.displayName} is no longer active.`));
    }

    if (entry.actorType === 'CustomerPortal') {
      const account = await deps.customerPortalAccounts.findActiveByUserId(entry.userId);
      if (!account || account.tenantId !== input.tenantId) {
        return Result.fail(makeError('Chat.Member.NoPortalAccount', `${entry.displayName} has no active portal account.`));
      }
      if (restrictToAssigned) {
        actorSeesAll ??= await seesAllCustomers(input.actor, deps.userPermissions);
        const allowed =
          actorSeesAll || (await deps.customerAssignments.isAssigned(input.tenantId, account.customerId, input.actor.userId));
        if (!allowed) {
          return Result.fail(makeError('Chat.Member.NotAssigned', `You are not assigned to ${entry.displayName}.`));
        }
      }
      admitted.push({ userId: entry.userId, displayName: entry.displayName, actorType: entry.actorType, customerId: account.customerId });
      continue;
    }

    if (entry.actorType !== 'TenantEmployee' && entry.actorType !== 'TenantAdmin') {
      return Result.fail(makeError('Chat.Member.NotAllowed', `${entry.displayName} cannot join office groups.`));
    }
    admitted.push({ userId: entry.userId, displayName: entry.displayName, actorType: entry.actorType, customerId: null });
  }

  const clients = input.existingClientCount + admitted.filter((m) => m.actorType === 'CustomerPortal').length;
  if (clients > 1 && !(input.settings.groupsAllowMultipleClients ?? false)) {
    return Result.fail(
      makeError('Chat.Member.SingleClientOnly', 'Only one client can be in a group. Your office can allow more in Settings.'),
    );
  }

  const onlyEmployees =
    input.actor.actorType === 'TenantEmployee' &&
    !input.existingHasNonEmployee &&
    admitted.every((m) => m.actorType === 'TenantEmployee');
  if (onlyEmployees && !input.settings.employeeToEmployeeChatEnabled) {
    return Result.fail(makeError('Chat.EmployeeToEmployeeDisabled', 'Employee-to-employee chat is disabled for this office.'));
  }

  return Result.ok(admitted);
}

/** El Tenant Admin administra grupos sin ser miembro (D-22); lo decide el tipo de actor, no un permiso suelto. */
export function isTenantAdminActor(actorType: string): boolean {
  return actorType === 'TenantAdmin';
}

/** Para los conteos de la política al agregar: clientes actuales y si hay alguien que no es empleado. */
export function groupComposition(participants: ReadonlyArray<{ actorType: string; isRemoved: boolean }>): {
  clientCount: number;
  hasNonEmployee: boolean;
} {
  const active = participants.filter((p) => !p.isRemoved);
  return {
    clientCount: active.filter((p) => p.actorType === 'CustomerPortal').length,
    hasNonEmployee: active.some((p) => p.actorType !== 'TenantEmployee'),
  };
}
