import type { PrismaClient } from '@prisma/client';
import type {
  CustomerPortalAccountReconcileOutcome,
  CustomerPortalAccountReconcileWriter,
  CustomerPortalAccountRepository,
  CustomerPortalAccountSnapshot,
} from '../../application/ports/customer-portal-account-repository.js';

export class PrismaCustomerPortalAccountRepository
  implements CustomerPortalAccountRepository, CustomerPortalAccountReconcileWriter
{
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

  async applyReconciled(snapshot: {
    customerId: string;
    tenantId: string;
    userId: string;
    isActive: boolean;
  }): Promise<CustomerPortalAccountReconcileOutcome> {
    if (snapshot.isActive) {
      await this.upsert(snapshot);
      return 'upserted';
    }
    // Inactiva: solo la fila de ese mismo usuario y si sigue activa (no pisa la cuenta nueva del cliente).
    const result = await this.prisma.customerPortalAccount.updateMany({
      where: { CustomerId: snapshot.customerId, UserId: snapshot.userId, IsActive: true },
      data: { IsActive: false },
    });
    return result.count > 0 ? 'deactivated' : 'unchanged';
  }

  async markInactiveByUserId(userId: string): Promise<void> {
    await this.prisma.customerPortalAccount.updateMany({
      where: { UserId: userId },
      data: { IsActive: false },
    });
  }

  async markActiveByUserId(userId: string): Promise<void> {
    await this.prisma.customerPortalAccount.updateMany({
      where: { UserId: userId },
      data: { IsActive: true },
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

  async findActiveByCustomerIds(customerIds: readonly string[]): Promise<CustomerPortalAccountSnapshot[]> {
    if (customerIds.length === 0) return [];
    const rows = await this.prisma.customerPortalAccount.findMany({
      where: { CustomerId: { in: [...customerIds] }, IsActive: true },
    });
    return rows.map((row) => ({
      customerId: row.CustomerId,
      tenantId: row.TenantId,
      userId: row.UserId,
      isActive: row.IsActive,
    }));
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
