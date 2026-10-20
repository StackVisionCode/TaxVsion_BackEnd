import type { PrismaClient } from '@prisma/client';
import type { PresenceAudienceRepository } from '../../application/ports/presence-audience-repository.js';

export class PrismaPresenceAudienceRepository implements PresenceAudienceRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findActivePortalUserIdsForPreparer(
    tenantId: string,
    preparerUserId: string,
  ): Promise<readonly string[]> {
    const assignments = await this.prisma.customerPreparerAssignment.findMany({
      where: { TenantId: tenantId, PreparerUserId: preparerUserId },
      select: { CustomerId: true },
    });
    if (assignments.length === 0) return [];

    const accounts = await this.prisma.customerPortalAccount.findMany({
      where: {
        TenantId: tenantId,
        CustomerId: { in: assignments.map((assignment) => assignment.CustomerId) },
        IsActive: true,
      },
      select: { UserId: true },
    });
    return [...new Set(accounts.map((account) => account.UserId))];
  }

  async findStaffUserIdsVisibleToPortalUser(
    tenantId: string,
    portalUserId: string,
  ): Promise<readonly string[]> {
    const accounts = await this.prisma.customerPortalAccount.findMany({
      where: { TenantId: tenantId, UserId: portalUserId, IsActive: true },
      select: { CustomerId: true },
    });
    if (accounts.length === 0) return [];

    const assignments = await this.prisma.customerPreparerAssignment.findMany({
      where: {
        TenantId: tenantId,
        CustomerId: { in: accounts.map((a) => a.CustomerId) },
      },
      select: { PreparerUserId: true },
    });
    return [...new Set(assignments.map((a) => a.PreparerUserId))];
  }
}
