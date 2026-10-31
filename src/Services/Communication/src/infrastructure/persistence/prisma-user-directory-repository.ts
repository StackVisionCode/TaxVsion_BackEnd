import type { DirectoryCursor, StaffDirectoryPager } from '../../application/ports/directory-pager.js';
import type { PrismaClient, UserDirectoryEntry } from '@prisma/client';
import type {
  UserDirectoryRepository,
  UserDirectoryEntrySnapshot,
} from '../../application/ports/user-directory-repository.js';

export class PrismaUserDirectoryRepository implements UserDirectoryRepository, StaffDirectoryPager {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    userId: string;
    tenantId: string;
    displayName: string;
    email: string;
    isActive: boolean;
    actorType?: string;
    avatarFileId?: string | null;
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
        AvatarFileId: snapshot.avatarFileId ?? null,
      },
      update: {
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
        // `profile_updated` no conoce actorType (el evento no lo lleva) — no
        // pisar el valor ya guardado por `registered` con el default.
        ...(snapshot.actorType !== undefined ? { ActorType: snapshot.actorType } : {}),
        // `registered` no trae foto: undefined deja la guardada.
        ...(snapshot.avatarFileId !== undefined ? { AvatarFileId: snapshot.avatarFileId } : {}),
      },
    });
  }

  async findByUserId(userId: string): Promise<UserDirectoryEntrySnapshot | null> {
    const row = await this.prisma.userDirectoryEntry.findUnique({ where: { UserId: userId } });
    if (!row) return null;
    return toSnapshot(row);
  }

  async findByUserIds(userIds: readonly string[]): Promise<UserDirectoryEntrySnapshot[]> {
    const uniqueUserIds = [...new Set(userIds.filter((userId) => userId.trim().length > 0))];
    if (uniqueUserIds.length === 0) return [];

    const rows = await this.prisma.userDirectoryEntry.findMany({
      where: { UserId: { in: uniqueUserIds } },
    });
    return rows.map(toSnapshot);
  }

  async markInactive(userId: string): Promise<void> {
    await this.prisma.userDirectoryEntry
      .update({ where: { UserId: userId }, data: { IsActive: false } })
      .catch(() => undefined);
  }

  async markActive(userId: string): Promise<void> {
    await this.prisma.userDirectoryEntry
      .update({ where: { UserId: userId }, data: { IsActive: true } })
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
        // Solo STAFF: excluir clientes de portal (`CustomerPortal`) e invitados (`Guest`). Sin esto un
        // cliente aparecía en el picker de "employees" al armar una invitación → se invitaba como Employee
        // → el link salía al CRM (`/meetings`) en vez del portal (`/portal/client/meetings/accept/...`).
        ActorType: { notIn: ['CustomerPortal', 'Guest'] },
        OR: [{ DisplayName: { contains: query } }, { Email: { contains: query } }],
      },
      orderBy: { DisplayName: 'asc' },
      take: limit,
    });
    return rows.map(toSnapshot);
  }

  /** F8c (T-14): página del rail Team por cursor (DisplayName, UserId); mismo filtro de staff que el buscador. */
  async pageStaff(input: {
    tenantId: string;
    query: string | null;
    excludeUserId: string;
    take: number;
    after: DirectoryCursor | null;
  }): Promise<UserDirectoryEntrySnapshot[]> {
    const { query, after } = input;
    const rows = await this.prisma.userDirectoryEntry.findMany({
      where: {
        TenantId: input.tenantId,
        IsActive: true,
        ActorType: { notIn: ['CustomerPortal', 'Guest'] },
        UserId: { not: input.excludeUserId },
        AND: [
          ...(query ? [{ OR: [{ DisplayName: { contains: query } }, { Email: { contains: query } }] }] : []),
          ...(after
            ? [{ OR: [{ DisplayName: { gt: after.displayName } }, { DisplayName: after.displayName, UserId: { gt: after.id } }] }]
            : []),
        ],
      },
      orderBy: [{ DisplayName: 'asc' }, { UserId: 'asc' }],
      take: input.take,
    });
    return rows.map(toSnapshot);
  }
}

function toSnapshot(row: UserDirectoryEntry): UserDirectoryEntrySnapshot {
  return {
    userId: row.UserId,
    tenantId: row.TenantId,
    displayName: row.DisplayName,
    email: row.Email,
    isActive: row.IsActive,
    actorType: row.ActorType,
    avatarFileId: row.AvatarFileId ?? null,
    updatedAtUtc: row.UpdatedAtUtc,
  };
}
