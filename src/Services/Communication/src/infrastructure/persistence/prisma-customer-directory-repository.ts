import type { CustomerDirectoryEntry, PrismaClient } from '@prisma/client';
import type {
  CustomerDirectoryRepository,
  CustomerDirectoryEntrySnapshot,
} from '../../application/ports/customer-directory-repository.js';

export class PrismaCustomerDirectoryRepository implements CustomerDirectoryRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    customerId: string;
    tenantId: string;
    displayName: string;
    email: string;
    isActive: boolean;
    avatarFileId?: string | null;
  }): Promise<void> {
    await this.prisma.customerDirectoryEntry.upsert({
      where: { CustomerId: snapshot.customerId },
      create: {
        CustomerId: snapshot.customerId,
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
        AvatarFileId: snapshot.avatarFileId ?? null,
      },
      update: {
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
        ...(snapshot.avatarFileId !== undefined ? { AvatarFileId: snapshot.avatarFileId } : {}),
      },
    });
  }

  async findByCustomerId(tenantId: string, customerId: string): Promise<CustomerDirectoryEntrySnapshot | null> {
    const row = await this.prisma.customerDirectoryEntry.findFirst({
      where: { CustomerId: customerId, TenantId: tenantId },
    });
    if (!row) return null;
    return toSnapshot(row);
  }

  async markInactive(customerId: string): Promise<void> {
    await this.prisma.customerDirectoryEntry
      .update({ where: { CustomerId: customerId }, data: { IsActive: false } })
      .catch(() => undefined);
  }

  async clearAvatar(customerId: string): Promise<void> {
    await this.prisma.customerDirectoryEntry.updateMany({
      where: { CustomerId: customerId, AvatarFileId: { not: null } },
      data: { AvatarFileId: null },
    });
  }

  async searchByDisplayNameOrEmail(
    tenantId: string,
    query: string,
    limit: number,
  ): Promise<CustomerDirectoryEntrySnapshot[]> {
    const rows = await this.prisma.customerDirectoryEntry.findMany({
      where: {
        TenantId: tenantId,
        IsActive: true,
        OR: [{ DisplayName: { contains: query } }, { Email: { contains: query } }],
      },
      orderBy: { DisplayName: 'asc' },
      take: limit,
    });
    return rows.map(toSnapshot);
  }
}

function toSnapshot(row: CustomerDirectoryEntry): CustomerDirectoryEntrySnapshot {
  return {
    customerId: row.CustomerId,
    tenantId: row.TenantId,
    displayName: row.DisplayName,
    email: row.Email,
    isActive: row.IsActive,
    avatarFileId: row.AvatarFileId ?? null,
    updatedAtUtc: row.UpdatedAtUtc,
  };
}
