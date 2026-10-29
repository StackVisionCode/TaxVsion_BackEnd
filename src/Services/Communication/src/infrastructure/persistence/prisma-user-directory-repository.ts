import type { PrismaClient, UserDirectoryEntry } from '@prisma/client';
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
