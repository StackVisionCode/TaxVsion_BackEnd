import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import type { TenantSettingsProvider } from '../ports/tenant-settings-provider.js';
import type {
  UserDirectoryEntrySnapshot,
  UserDirectoryRepository,
} from '../ports/user-directory-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { customerCanChatWithStaff } from './customer-chat-access.js';
import { resolveAvatars, type AvatarResolver } from './resolve-avatars.js';

export type OfficeContactRelation = 'preparer' | 'team' | 'office';

export interface OfficeContact {
  readonly userId: string;
  readonly displayName: string;
  readonly isPrimaryPreparer: boolean;
  readonly relation: OfficeContactRelation;
  readonly avatarFileId: string | null;
}

// PlatformAdmin no se ofrece como contacto de la oficina.
const OFFICE_STAFF = new Set(['TenantEmployee', 'TenantAdmin']);
const RANK: Record<OfficeContactRelation, number> = { preparer: 0, team: 1, office: 2 };
const key = (id: string): string => id.toLowerCase();

/**
 * A quién puede escribirle el cliente del portal: su staff asignado y los admins de su oficina,
 * filtrados con la misma regla que `startDirectConversation`. Nunca otros clientes ni otro tenant.
 */
export async function getMyOfficeContact(
  p: { tenantId: string; userId: string; actorType: string },
  deps: {
    customerPortalAccounts: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'>;
    customerPreparerAssignments: Pick<CustomerPreparerAssignmentRepository, 'findByCustomerId'>;
    customerAssignments: Pick<CustomerAssignmentProjectionRepository, 'getAssignedUserIds'>;
    userDirectory: Pick<UserDirectoryRepository, 'findByUserId' | 'findByUserIds' | 'findActiveAdmins'>;
    userPermissions: UserPermissionsProjectionRepository;
    settings: TenantSettingsProvider;
    assignmentVisibilityEnabled?: boolean | undefined;
    avatars?: AvatarResolver | undefined;
  },
): Promise<OfficeContact[]> {
  if (p.actorType !== 'CustomerPortal') return [];
  const portal = await deps.customerPortalAccounts.findActiveByUserId(p.userId);
  if (!portal || !portal.isActive || key(portal.tenantId) !== key(p.tenantId)) return [];
  const settings = await deps.settings.get(p.tenantId);
  if (!settings.chatEnabled) return [];

  const [primary, assignedIds, admins] = await Promise.all([
    deps.customerPreparerAssignments.findByCustomerId(p.tenantId, portal.customerId),
    deps.customerAssignments.getAssignedUserIds(p.tenantId, portal.customerId),
    deps.userDirectory.findActiveAdmins(p.tenantId),
  ]);

  // Relación de mayor prioridad por usuario: preparer > team > office.
  const relations = new Map<string, OfficeContactRelation>();
  const offer = (userId: string, relation: OfficeContactRelation): void => {
    const current = relations.get(key(userId));
    if (!current || RANK[relation] < RANK[current]) relations.set(key(userId), relation);
  };
  if (primary && key(primary.tenantId) === key(p.tenantId)) offer(primary.preparerUserId, 'preparer');
  for (const id of assignedIds) offer(id, 'team');
  for (const admin of admins) offer(admin.userId, 'office');

  const entries = new Map<string, UserDirectoryEntrySnapshot>(admins.map((a) => [key(a.userId), a]));
  const missing = [...relations.keys()].filter((id) => !entries.has(id));
  const found = deps.userDirectory.findByUserIds
    ? await deps.userDirectory.findByUserIds(missing)
    : await Promise.all(missing.map((id) => deps.userDirectory.findByUserId(id)));
  for (const entry of found) if (entry) entries.set(key(entry.userId), entry);

  // El set M:N ya está cargado: la regla consulta la asignación en memoria.
  const assigned = new Set(assignedIds.map(key));
  const access = {
    customerAssignments: {
      isAssigned: async (_t: string, _c: string, userId: string) => assigned.has(key(userId)),
    },
    userPermissions: deps.userPermissions,
    assignmentVisibilityEnabled: deps.assignmentVisibilityEnabled,
  };
  const candidates = [...relations.entries()].flatMap(([id, relation]) => {
    const entry = entries.get(id);
    const valid =
      entry && entry.isActive && key(entry.tenantId) === key(p.tenantId) && OFFICE_STAFF.has(entry.actorType);
    return valid ? [{ entry, relation }] : [];
  });
  const allowed = await Promise.all(
    candidates.map((c) =>
      customerCanChatWithStaff(
        { tenantId: p.tenantId, customerId: portal.customerId, staff: c.entry },
        settings,
        access,
      ),
    ),
  );
  const contacts = candidates
    .filter((_, i) => allowed[i])
    .sort(
      (a, b) => RANK[a.relation] - RANK[b.relation] || a.entry.displayName.localeCompare(b.entry.displayName),
    );

  const avatars = await resolveAvatars(
    deps.avatars,
    contacts.map((c) => c.entry.userId),
  );
  return contacts.map(({ entry, relation }) => ({
    userId: entry.userId,
    displayName: entry.displayName,
    isPrimaryPreparer: relation === 'preparer',
    relation,
    avatarFileId: avatars?.of(entry.userId) ?? null,
  }));
}
