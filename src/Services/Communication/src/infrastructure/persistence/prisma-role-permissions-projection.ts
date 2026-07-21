import type { PrismaClient } from '@prisma/client';
import type {
  RolePermissionsProjectionRepository,
  RolePermissionsProjectionSnapshot,
} from '../../application/ports/role-permissions-projection-repository.js';

export class PrismaRolePermissionsProjectionRepository implements RolePermissionsProjectionRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async upsert(snapshot: {
    roleId: string;
    tenantId: string;
    roleName: string;
    permissionCodes: readonly string[];
    permissionsVersion: number;
  }): Promise<void> {
    await this.prisma.rolePermissionsProjection.upsert({
      where: { RoleId: snapshot.roleId },
      create: {
        RoleId: snapshot.roleId,
        TenantId: snapshot.tenantId,
        RoleName: snapshot.roleName,
        PermissionCodes: JSON.stringify(snapshot.permissionCodes),
        PermissionsVersion: snapshot.permissionsVersion,
      },
      update: {
        TenantId: snapshot.tenantId,
        RoleName: snapshot.roleName,
        PermissionCodes: JSON.stringify(snapshot.permissionCodes),
        PermissionsVersion: snapshot.permissionsVersion,
      },
    });
  }

  async findByRoleIds(roleIds: readonly string[]): Promise<readonly RolePermissionsProjectionSnapshot[]> {
    if (roleIds.length === 0) return [];
    const rows = await this.prisma.rolePermissionsProjection.findMany({
      where: { RoleId: { in: [...roleIds] } },
    });
    return rows.map((row) => ({
      roleId: row.RoleId,
      tenantId: row.TenantId,
      roleName: row.RoleName,
      permissionCodes: safeParseStringArray(row.PermissionCodes),
      permissionsVersion: row.PermissionsVersion,
      updatedAtUtc: row.UpdatedAtUtc,
    }));
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
