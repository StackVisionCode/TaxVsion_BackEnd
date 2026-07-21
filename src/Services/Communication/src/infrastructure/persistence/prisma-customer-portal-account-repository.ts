import type { PrismaClient } from '@prisma/client';
import type {
  CustomerPortalAccountRepository,
  CustomerPortalAccountSnapshot,
} from '../../application/ports/customer-portal-account-repository.js';

export class PrismaCustomerPortalAccountRepository implements CustomerPortalAccountRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: { customerId: string; tenantId: string; userId: string }): Promise<void> {
    await this.prisma.customerPortalAccount.upsert({
      where: { CustomerId: snapshot.customerId },
      create: {
        CustomerId: snapshot.customerId,
        TenantId: snapshot.tenantId,
        UserId: snapshot.userId,
        IsActive: true,
      },
      update: {
        TenantId: snapshot.tenantId,
        UserId: snapshot.userId,
        IsActive: true,
      },
    });
  }

  async markInactiveByUserId(userId: string): Promise<void> {
    await this.prisma.customerPortalAccount.updateMany({
      where: { UserId: userId },
      data: { IsActive: false },
    });
  }

  async findActiveByCustomerId(customerId: string): Promise<CustomerPortalAccountSnapshot | null> {
    const row = await this.prisma.customerPortalAccount.findFirst({
      where: { CustomerId: customerId, IsActive: true },
    });
    if (!row) return null;
    return {
      customerId: row.CustomerId,
      tenantId: row.TenantId,
      userId: row.UserId,
      isActive: row.IsActive,
    };
  }

  async findActiveByUserId(userId: string): Promise<CustomerPortalAccountSnapshot | null> {
    const row = await this.prisma.customerPortalAccount.findFirst({
      where: { UserId: userId, IsActive: true },
    });
    if (!row) return null;
    return {
      customerId: row.CustomerId,
      tenantId: row.TenantId,
      userId: row.UserId,
      isActive: row.IsActive,
    };
  }
}
