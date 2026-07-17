import type { PrismaClient } from '@prisma/client';
import type {
  UserDirectoryRepository,
  UserDirectoryEntrySnapshot,
} from '../../application/ports/user-directory-repository.js';

export class PrismaUserDirectoryRepository implements UserDirectoryRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    userId: string;
    tenantId: string;
    displayName: string;
    email: string;
    isActive: boolean;
    actorType?: string;
  }): Promise<void> {
    await this.prisma.userDirectoryEntry.upsert({
      where: { UserId: snapshot.userId },
      create: {
        UserId: snapshot.userId,
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
        ActorType: snapshot.actorType ?? 'TenantEmployee',
      },
      update: {
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
        // `profile_updated` no conoce actorType (el evento no lo lleva) — no
        // pisar el valor ya guardado por `registered` con el default.
        ...(snapshot.actorType !== undefined ? { ActorType: snapshot.actorType } : {}),
      },
    });
  }

  async findByUserId(userId: string): Promise<UserDirectoryEntrySnapshot | null> {
    const row = await this.prisma.userDirectoryEntry.findUnique({ where: { UserId: userId } });
    if (!row) return null;
    return {
      userId: row.UserId,
      tenantId: row.TenantId,
      displayName: row.DisplayName,
      email: row.Email,
      isActive: row.IsActive,
      actorType: row.ActorType,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }

  async markInactive(userId: string): Promise<void> {
    await this.prisma.userDirectoryEntry
      .update({ where: { UserId: userId }, data: { IsActive: false } })
      .catch(() => undefined);
  }

  async searchByDisplayNameOrEmail(
    tenantId: string,
    query: string,
    limit: number,
  ): Promise<UserDirectoryEntrySnapshot[]> {
    const rows = await this.prisma.userDirectoryEntry.findMany({
      where: {
        TenantId: tenantId,
        IsActive: true,
        OR: [{ DisplayName: { contains: query } }, { Email: { contains: query } }],
      },
      orderBy: { DisplayName: 'asc' },
      take: limit,
    });
    return rows.map((row) => ({
      userId: row.UserId,
      tenantId: row.TenantId,
      displayName: row.DisplayName,
      email: row.Email,
      isActive: row.IsActive,
      actorType: row.ActorType,
      updatedAtUtc: row.UpdatedAtUtc,
    }));
  }
}
