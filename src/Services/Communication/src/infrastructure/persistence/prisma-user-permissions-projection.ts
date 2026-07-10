import type { PrismaClient } from '@prisma/client';
import type {
  UserPermissionsProjectionRepository,
  UserPermissionsProjectionSnapshot,
} from '../../application/ports/user-permissions-projection-repository.js';

export class PrismaUserPermissionsProjectionRepository implements UserPermissionsProjectionRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    userId: string;
    tenantId: string;
    permissions: readonly string[];
    permissionVersion: number;
    actorType: string;
    isActive: boolean;
    updatedAtUtc: Date;
  }): Promise<void> {
    await this.prisma.userPermissionsProjection.upsert({
      where: { UserId: snapshot.userId },
      create: {
        UserId: snapshot.userId,
        TenantId: snapshot.tenantId,
        Permissions: JSON.stringify(snapshot.permissions),
        PermVersion: snapshot.permissionVersion,
        ActorType: snapshot.actorType,
        IsActive: snapshot.isActive,
      },
      update: {
        TenantId: snapshot.tenantId,
        Permissions: JSON.stringify(snapshot.permissions),
        PermVersion: snapshot.permissionVersion,
        ActorType: snapshot.actorType,
        IsActive: snapshot.isActive,
      },
    });
  }

  async findByUserId(userId: string): Promise<UserPermissionsProjectionSnapshot | null> {
    const row = await this.prisma.userPermissionsProjection.findUnique({ where: { UserId: userId } });
    if (!row) return null;
    const perms = safeParseStringArray(row.Permissions);
    return {
      userId: row.UserId,
      tenantId: row.TenantId,
      permissions: perms,
      permissionVersion: row.PermVersion,
      actorType: row.ActorType,
      isActive: row.IsActive,
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }

  async markInactive(userId: string, _now: Date): Promise<void> {
    await this.prisma.userPermissionsProjection
      .update({ where: { UserId: userId }, data: { IsActive: false } })
      .catch(() => undefined);
  }
}

function safeParseStringArray(raw: string): readonly string[] {
  try {
    const parsed: unknown = JSON.parse(raw);
    if (Array.isArray(parsed)) {
      return parsed.filter((v: unknown): v is string => typeof v === 'string');
    }
    return [];
  } catch {
    return [];
  }
}
