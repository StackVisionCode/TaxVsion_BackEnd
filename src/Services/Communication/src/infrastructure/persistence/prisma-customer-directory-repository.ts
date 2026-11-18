import { Prisma, type CustomerDirectoryEntry, type PrismaClient } from '@prisma/client';
import type { CustomerDirectoryPager, DirectoryCursor } from '../../application/ports/directory-pager.js';
import type {
  CustomerDirectoryRepository,
  CustomerDirectoryEntrySnapshot,
} from '../../application/ports/customer-directory-repository.js';
import { newerProfile, newerStatus, stamp } from './status-order.js';

export class PrismaCustomerDirectoryRepository
  implements CustomerDirectoryRepository, CustomerDirectoryPager
{
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    customerId: string;
    tenantId: string;
    displayName: string;
    email: string | null;
    isActive?: boolean;
    avatarFileId?: string | null;
    occurredAtUtc?: Date;
  }): Promise<void> {
    const at = snapshot.occurredAtUtc;
    // Prisma no admite una condición en `upsert`: se actualiza solo si el aviso es más nuevo, y si no hay fila se crea.
    const updated = await this.updateProfile(snapshot);
    if (updated === 0 && !(await this.exists(snapshot.customerId))) {
      try {
        await this.prisma.customerDirectoryEntry.create({
          data: {
            CustomerId: snapshot.customerId,
            TenantId: snapshot.tenantId,
            DisplayName: snapshot.displayName,
            Email: snapshot.email,
            IsActive: snapshot.isActive ?? true,
            AvatarFileId: snapshot.avatarFileId ?? null,
            ProfileChangedAtUtc: at ?? null,
            StatusChangedAtUtc: snapshot.isActive !== undefined ? (at ?? null) : null,
          },
        });
        return;
      } catch (err) {
        // Otro aviso la creó al mismo tiempo: se aplica como actualización.
        if (!(err instanceof Prisma.PrismaClientKnownRequestError) || err.code !== 'P2002') throw err;
        await this.updateProfile(snapshot);
      }
    }
    // El estado sigue el orden de los avisos de estado: un cambio viejo no reactiva a un cliente desactivado.
    if (snapshot.isActive !== undefined) {
      await this.prisma.customerDirectoryEntry.updateMany({
        where: { CustomerId: snapshot.customerId, ...newerStatus(at) },
        data: { IsActive: snapshot.isActive, ...stamp(at) },
      });
    }
  }

  private async updateProfile(snapshot: {
    customerId: string;
    tenantId: string;
    displayName: string;
    email: string | null;
    avatarFileId?: string | null;
    occurredAtUtc?: Date;
  }): Promise<number> {
    const at = snapshot.occurredAtUtc;
    const result = await this.prisma.customerDirectoryEntry.updateMany({
      where: { CustomerId: snapshot.customerId, ...newerProfile(at) },
      data: {
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        // E-1: el email no se quita; un aviso sin email conserva el conocido.
        ...(snapshot.email !== null ? { Email: snapshot.email } : {}),
        ...(snapshot.avatarFileId !== undefined ? { AvatarFileId: snapshot.avatarFileId } : {}),
        ...(at ? { ProfileChangedAtUtc: at } : {}),
      },
    });
    return result.count;
  }

  private async exists(customerId: string): Promise<boolean> {
    const row = await this.prisma.customerDirectoryEntry.findUnique({
      where: { CustomerId: customerId },
      select: { CustomerId: true },
    });
    return row !== null;
  }

  async findByCustomerId(
    tenantId: string,
    customerId: string,
  ): Promise<CustomerDirectoryEntrySnapshot | null> {
    const row = await this.prisma.customerDirectoryEntry.findFirst({
      where: { CustomerId: customerId, TenantId: tenantId },
    });
    if (!row) return null;
    return toSnapshot(row);
  }

  async findActiveByEmail(tenantId: string, email: string): Promise<CustomerDirectoryEntrySnapshot[]> {
    // La intercalacion de SQL Server no distingue mayusculas: el email se compara tal cual.
    const rows = await this.prisma.customerDirectoryEntry.findMany({
      where: { TenantId: tenantId, Email: email.trim(), IsActive: true },
      take: 2,
    });
    return rows.map(toSnapshot);
  }

  async markInactive(customerId: string, statusChangedAtUtc?: Date): Promise<void> {
    await this.prisma.customerDirectoryEntry.updateMany({
      where: { CustomerId: customerId, ...newerStatus(statusChangedAtUtc) },
      data: { IsActive: false, ...stamp(statusChangedAtUtc) },
    });
  }

  async markActive(customerId: string, statusChangedAtUtc?: Date): Promise<void> {
    await this.prisma.customerDirectoryEntry.updateMany({
      where: { CustomerId: customerId, ...newerStatus(statusChangedAtUtc) },
      data: { IsActive: true, ...stamp(statusChangedAtUtc) },
    });
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
    const conditions: Prisma.Sql[] = [
      Prisma.sql`d.[TenantId] = ${input.tenantId}`,
      Prisma.sql`d.[IsActive] = 1`,
    ];
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
