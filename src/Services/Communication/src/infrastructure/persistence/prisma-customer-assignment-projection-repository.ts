import type { PrismaClient } from '@prisma/client';
import type { CustomerAssignmentProjectionRepository } from '../../application/ports/customer-assignment-projection-repository.js';

export class PrismaCustomerAssignmentProjectionRepository implements CustomerAssignmentProjectionRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async getVersion(tenantId: string, customerId: string): Promise<Date | null> {
    const rows = await this.prisma.customerAssignmentProjection.findMany({
      where: { TenantId: tenantId, CustomerId: customerId },
      select: { Version: true },
    });
    if (rows.length === 0) return null;
    return rows.reduce<Date>((max, r) => (r.Version > max ? r.Version : max), rows[0]!.Version);
  }

  async replace(
    tenantId: string,
    customerId: string,
    userIds: readonly string[],
    version: Date,
  ): Promise<void> {
    const distinct = [...new Set(userIds)];
    // Borra + inserta el set en una transaccion: el consumer/reconciliador reemplaza, no mergea.
    await this.prisma.$transaction([
      this.prisma.customerAssignmentProjection.deleteMany({
        where: { TenantId: tenantId, CustomerId: customerId },
      }),
      ...distinct.map((userId) =>
        this.prisma.customerAssignmentProjection.create({
          data: { TenantId: tenantId, CustomerId: customerId, UserId: userId, Version: version },
        }),
      ),
    ]);
  }

  async isAssigned(tenantId: string, customerId: string, userId: string): Promise<boolean> {
    const row = await this.prisma.customerAssignmentProjection.findFirst({
      where: { TenantId: tenantId, CustomerId: customerId, UserId: userId },
      select: { Id: true },
    });
    return row !== null;
  }

  async getAssignedCustomerIds(tenantId: string, userId: string): Promise<string[]> {
    const rows = await this.prisma.customerAssignmentProjection.findMany({
      where: { TenantId: tenantId, UserId: userId },
      select: { CustomerId: true },
    });
    return [...new Set(rows.map((r) => r.CustomerId))];
  }
}
