import type { PrismaClient } from '@prisma/client';
import type { TenantTimeZoneStore } from '../../application/ports/tenant-time-zone-store.js';
import type { TenantTimeZone } from '../../domain/time-zones/tenant-time-zone.js';

export class PrismaTenantTimeZoneStore implements TenantTimeZoneStore {
  constructor(private readonly prisma: PrismaClient) {}

  async find(tenantId: string): Promise<TenantTimeZone | null> {
    const row = await this.prisma.tenantTimeZoneProjection.findUnique({ where: { TenantId: tenantId } });
    if (!row) return null;
    return {
      tenantId: row.TenantId,
      timeZoneId: row.TimeZoneId,
      sourceVersion: row.SourceVersion === null ? null : Number(row.SourceVersion),
      sourceUpdatedAtUtc: row.SourceUpdatedAtUtc,
    };
  }

  async save(zone: TenantTimeZone): Promise<void> {
    const data = {
      TimeZoneId: zone.timeZoneId,
      SourceVersion: zone.sourceVersion === null ? null : BigInt(zone.sourceVersion),
      SourceUpdatedAtUtc: zone.sourceUpdatedAtUtc,
    };
    await this.prisma.tenantTimeZoneProjection.upsert({
      where: { TenantId: zone.tenantId },
      create: { TenantId: zone.tenantId, ...data },
      update: data,
    });
  }
}
