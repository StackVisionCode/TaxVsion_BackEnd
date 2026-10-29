import { Prisma, type PrismaClient } from '@prisma/client';
import type {
  AvatarLookupRepository,
  AvatarLookupRow,
} from '../../application/ports/avatar-lookup-repository.js';

interface RawAvatarRow {
  readonly UserId: string;
  readonly ActorType: string | null;
  readonly UserAvatarFileId: string | null;
  readonly CustomerAvatarFileId: string | null;
  readonly HasActivePortalAccount: boolean | number;
}

/**
 * Una sola consulta para N usuarios: parte de la lista pedida (VALUES), asi un cliente del portal sin
 * fila en UserDirectoryEntry igual resuelve por su cuenta de portal. TRY_CONVERT descarta ids que no
 * son GUID en vez de romper la consulta. OUTER APPLY TOP 1 porque CustomerPortalAccount.UserId no es
 * unico en el esquema.
 */
export class PrismaAvatarLookupRepository implements AvatarLookupRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async findByUserIds(userIds: readonly string[]): Promise<AvatarLookupRow[]> {
    if (userIds.length === 0) return [];
    const values = Prisma.join(userIds.map((id) => Prisma.sql`(TRY_CONVERT(UNIQUEIDENTIFIER, ${id}))`));

    const rows = await this.prisma.$queryRaw<RawAvatarRow[]>`
      SELECT
        LOWER(CONVERT(NVARCHAR(36), ids.UserId)) AS UserId,
        ud.ActorType AS ActorType,
        LOWER(CONVERT(NVARCHAR(36), ud.AvatarFileId)) AS UserAvatarFileId,
        LOWER(CONVERT(NVARCHAR(36), portal.AvatarFileId)) AS CustomerAvatarFileId,
        CAST(CASE WHEN portal.CustomerId IS NULL THEN 0 ELSE 1 END AS BIT) AS HasActivePortalAccount
      FROM (VALUES ${values}) AS ids(UserId)
      LEFT JOIN [dbo].[UserDirectoryEntry] ud ON ud.UserId = ids.UserId
      OUTER APPLY (
        SELECT TOP 1 cpa.CustomerId, cd.AvatarFileId
        FROM [dbo].[CustomerPortalAccount] cpa
        LEFT JOIN [dbo].[CustomerDirectoryEntry] cd
          ON cd.CustomerId = cpa.CustomerId AND cd.TenantId = cpa.TenantId
        WHERE cpa.UserId = ids.UserId AND cpa.IsActive = 1
        ORDER BY cpa.UpdatedAtUtc DESC
      ) portal
      WHERE ids.UserId IS NOT NULL`;

    return rows.map((row) => ({
      userId: row.UserId,
      actorType: row.ActorType,
      userAvatarFileId: row.UserAvatarFileId,
      customerAvatarFileId: row.CustomerAvatarFileId,
      hasActivePortalAccount: row.HasActivePortalAccount === true || row.HasActivePortalAccount === 1,
    }));
  }
}
