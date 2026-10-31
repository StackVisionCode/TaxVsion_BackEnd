import type { PrismaClient } from '@prisma/client';
import type { CallUsageRepository } from '../../application/ports/call-usage-repository.js';

export class PrismaCallUsageRepository implements CallUsageRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async addSeconds(input: { tenantId: string; yearMonth: string; seconds: number }): Promise<void> {
    if (input.seconds <= 0) return;
    // MERGE atomico: dos finales de llamada simultaneos no pisan el contador.
    await this.prisma.$executeRaw`
      MERGE [dbo].[TenantCallUsage] WITH (HOLDLOCK) AS target
      USING (SELECT ${input.tenantId} AS TenantId, ${input.yearMonth} AS YearMonth) AS src
        ON target.[TenantId] = src.TenantId AND target.[YearMonth] = src.YearMonth
      WHEN MATCHED THEN UPDATE SET [Seconds] = target.[Seconds] + ${input.seconds}, [UpdatedAtUtc] = SYSUTCDATETIME()
      WHEN NOT MATCHED THEN INSERT ([TenantId], [YearMonth], [Seconds], [UpdatedAtUtc])
        VALUES (src.TenantId, src.YearMonth, ${input.seconds}, SYSUTCDATETIME());`;
  }

  async getSeconds(input: { tenantId: string; yearMonth: string }): Promise<number> {
    const row = await this.prisma.tenantCallUsage.findUnique({
      where: { TenantId_YearMonth: { TenantId: input.tenantId, YearMonth: input.yearMonth } },
    });
    return row?.Seconds ?? 0;
  }
}
