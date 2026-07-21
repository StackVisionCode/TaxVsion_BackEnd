import type { PrismaClient } from '@prisma/client';
import type {
  CustomerPreparerAssignmentRepository,
  CustomerPreparerAssignmentSnapshot,
} from '../../application/ports/customer-preparer-assignment-repository.js';

export class PrismaCustomerPreparerAssignmentRepository implements CustomerPreparerAssignmentRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async assign(input: { customerId: string; tenantId: string; preparerUserId: string }): Promise<void> {
    await this.prisma.customerPreparerAssignment.upsert({
      where: { CustomerId: input.customerId },
      create: {
        CustomerId: input.customerId,
        TenantId: input.tenantId,
        PreparerUserId: input.preparerUserId,
      },
      update: {
        TenantId: input.tenantId,
        PreparerUserId: input.preparerUserId,
      },
    });
  }

  async unassign(customerId: string): Promise<void> {
    await this.prisma.customerPreparerAssignment.delete({ where: { CustomerId: customerId } }).catch(() => undefined);
  }

  async findByCustomerId(tenantId: string, customerId: string): Promise<CustomerPreparerAssignmentSnapshot | null> {
    const row = await this.prisma.customerPreparerAssignment.findFirst({
      where: { CustomerId: customerId, TenantId: tenantId },
    });
    if (!row) return null;
    return {
      customerId: row.CustomerId,
      tenantId: row.TenantId,
      preparerUserId: row.PreparerUserId,
      assignedAtUtc: row.AssignedAtUtc,
    };
  }
}
