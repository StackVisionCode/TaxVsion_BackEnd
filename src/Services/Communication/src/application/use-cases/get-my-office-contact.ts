import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../ports/customer-preparer-assignment-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
/** The portal resolves its own preparer, never an arbitrary customer or office. */
export async function getMyOfficeContact(
  p: { tenantId: string; userId: string; actorType: string },
  deps: {
    customerPortalAccounts: CustomerPortalAccountRepository;
    customerPreparerAssignments: CustomerPreparerAssignmentRepository;
    userDirectory: UserDirectoryRepository;
  },
): Promise<{ userId: string; displayName: string; isPrimaryPreparer: boolean }[]> {
  if (p.actorType !== 'CustomerPortal') return [];
  const portal = await deps.customerPortalAccounts.findActiveByUserId(p.userId);
  if (!portal || !portal.isActive || portal.tenantId !== p.tenantId) return [];
  const assignment = await deps.customerPreparerAssignments.findByCustomerId(p.tenantId, portal.customerId);
  if (!assignment || assignment.tenantId !== p.tenantId) return [];
  const employee = await deps.userDirectory.findByUserId(assignment.preparerUserId);
  if (
    !employee ||
    !employee.isActive ||
    employee.tenantId !== p.tenantId ||
    !['TenantEmployee', 'TenantAdmin', 'PlatformAdmin'].includes(employee.actorType)
  )
    return [];
  return [{ userId: employee.userId, displayName: employee.displayName, isPrimaryPreparer: true }];
}
