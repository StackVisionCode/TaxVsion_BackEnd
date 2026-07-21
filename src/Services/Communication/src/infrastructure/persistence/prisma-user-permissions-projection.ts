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
    roleIds: readonly string[];
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
        RoleIds: JSON.stringify(snapshot.roleIds),
        ActorType: snapshot.actorType,
        IsActive: snapshot.isActive,
      },
      update: {
        TenantId: snapshot.tenantId,
        Permissions: JSON.stringify(snapshot.permissions),
        PermVersion: snapshot.permissionVersion,
        RoleIds: JSON.stringify(snapshot.roleIds),
        ActorType: snapshot.actorType,
        IsActive: snapshot.isActive,
      },
    });
  }

  async findByUserId(userId: string): Promise<UserPermissionsProjectionSnapshot | null> {
    const row = await this.prisma.userPermissionsProjection.findUnique({ where: { UserId: userId } });
    if (!row) return null;
    return toSnapshot(row);
  }

  async markInactive(userId: string, _now: Date): Promise<void> {
    await this.prisma.userPermissionsProjection
      .update({ where: { UserId: userId }, data: { IsActive: false } })
      .catch(() => undefined);
  }

  async findActiveByTenantAndRoleId(
    tenantId: string,
    roleId: string,
  ): Promise<readonly UserPermissionsProjectionSnapshot[]> {
    // RoleIds se guarda como JSON string ("[\"<guid>\",...]") — `contains` sobre el string
    // entrecomillado es seguro porque los GUID son de largo fijo, nunca matchea parcialmente
    // a otro roleId.
    const rows = await this.prisma.userPermissionsProjection.findMany({
      where: { TenantId: tenantId, IsActive: true, RoleIds: { contains: `"${roleId}"` } },
    });
    return rows.map(toSnapshot);
  }
}

function toSnapshot(row: {
  UserId: string;
  TenantId: string;
  Permissions: string;
  PermVersion: number;
  RoleIds: string;
  ActorType: string;
  IsActive: boolean;
  UpdatedAtUtc: Date;
}): UserPermissionsProjectionSnapshot {
  return {
    userId: row.UserId,
    tenantId: row.TenantId,
    permissions: safeParseStringArray(row.Permissions),
    permissionVersion: row.PermVersion,
    roleIds: safeParseStringArray(row.RoleIds),
    actorType: row.ActorType,
    isActive: row.IsActive,
    updatedAtUtc: row.UpdatedAtUtc,
  };
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
