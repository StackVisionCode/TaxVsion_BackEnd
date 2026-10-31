import { Prisma, type CustomerDirectoryEntry, type PrismaClient } from '@prisma/client';
import type { CustomerDirectoryPager, DirectoryCursor } from '../../application/ports/directory-pager.js';
import type {
  CustomerDirectoryRepository,
  CustomerDirectoryEntrySnapshot,
} from '../../application/ports/customer-directory-repository.js';

export class PrismaCustomerDirectoryRepository implements CustomerDirectoryRepository, CustomerDirectoryPager {
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

  /**
   * F8c (T-14): página del rail Clients por cursor (DisplayName, CustomerId). Con `assignedToUserId` el ABAC va
   * en la misma consulta (EXISTS sobre la proyección de asignaciones), así la página trae `take` filas visibles.
   * Los opcionales se agregan como fragmentos, nunca como parámetro NULL (Prisma los tipa int en SQL Server).
   */
  async pageCustomers(input: {
    tenantId: string;
    query: string | null;
    take: number;
    after: DirectoryCursor | null;
    assignedToUserId: string | null;
  }): Promise<CustomerDirectoryEntrySnapshot[]> {
    const conditions: Prisma.Sql[] = [Prisma.sql`d.[TenantId] = ${input.tenantId}`, Prisma.sql`d.[IsActive] = 1`];
    if (input.query) {
      const like = `%${escapeLike(input.query)}%`;
      conditions.push(Prisma.sql`(d.[DisplayName] LIKE ${like} OR d.[Email] LIKE ${like})`);
    }
    if (input.after) {
      conditions.push(
        Prisma.sql`(d.[DisplayName] > ${input.after.displayName} OR (d.[DisplayName] = ${input.after.displayName} AND d.[CustomerId] > ${input.after.id}))`,
      );
    }
    if (input.assignedToUserId) {
      conditions.push(Prisma.sql`EXISTS (SELECT 1 FROM [dbo].[CustomerAssignmentProjection] a
        WHERE a.[TenantId] = d.[TenantId] AND a.[CustomerId] = d.[CustomerId] AND a.[UserId] = ${input.assignedToUserId})`);
    }
    const rows = await this.prisma.$queryRaw<CustomerDirectoryEntry[]>`
      SELECT TOP (${input.take}) d.[CustomerId], d.[TenantId], d.[DisplayName], d.[Email], d.[IsActive], d.[AvatarFileId], d.[UpdatedAtUtc]
      FROM [dbo].[CustomerDirectoryEntry] d
      WHERE ${Prisma.join(conditions, ' AND ')}
      ORDER BY d.[DisplayName] ASC, d.[CustomerId] ASC`;
    return rows.map((row) => ({
      customerId: row.CustomerId,
      tenantId: row.TenantId,
      displayName: row.DisplayName,
      email: row.Email,
      isActive: Boolean(row.IsActive),
      avatarFileId: row.AvatarFileId,
      updatedAtUtc: row.UpdatedAtUtc,
    }));
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

/** LIKE de SQL Server: `%`, `_` y `[` del término se buscan literales. */
function escapeLike(value: string): string {
  return value.replace(/[[%_]/g, (ch) => `[${ch}]`);
}
