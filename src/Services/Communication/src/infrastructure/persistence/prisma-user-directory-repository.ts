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
  }): Promise<void> {
    await this.prisma.userDirectoryEntry.upsert({
      where: { UserId: snapshot.userId },
      create: {
        UserId: snapshot.userId,
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
      },
      update: {
        TenantId: snapshot.tenantId,
        DisplayName: snapshot.displayName,
        Email: snapshot.email,
        IsActive: snapshot.isActive,
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
      updatedAtUtc: row.UpdatedAtUtc,
    };
  }

  async markInactive(userId: string): Promise<void> {
    await this.prisma.userDirectoryEntry
      .update({ where: { UserId: userId }, data: { IsActive: false } })
      .catch(() => undefined);
  }
}
