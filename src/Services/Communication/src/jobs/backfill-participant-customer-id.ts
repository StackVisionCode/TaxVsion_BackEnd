import { connectPrisma, prisma } from '../infrastructure/persistence/prisma-client.js';
import { logger } from '../infrastructure/logger/logger.js';

/**
 * Backfill one-shot (D-58): rellena `MeetingParticipant.CustomerId` de las filas anteriores a F3 a
 * partir de la proyección CustomerPortalAccount (UserId → CustomerId). Idempotente: solo toca filas
 * con CustomerId nulo. `--dry-run` solo cuenta.
 *
 *   npx tsx src/jobs/backfill-participant-customer-id.ts --dry-run
 *   node dist/jobs/backfill-participant-customer-id.js
 */
async function main(): Promise<void> {
  const dryRun = process.argv.includes('--dry-run');
  await connectPrisma();

  const pending = await prisma.$queryRaw<Array<{ total: number }>>`
    SELECT COUNT(*) AS total
    FROM [dbo].[MeetingParticipant] p
    INNER JOIN [dbo].[CustomerPortalAccount] a ON a.[UserId] = p.[UserId] AND a.[TenantId] = p.[TenantId]
    WHERE p.[CustomerId] IS NULL`;
  const candidates = Number(pending[0]?.total ?? 0);

  let updated = 0;
  if (!dryRun && candidates > 0) {
    updated = await prisma.$executeRaw`
      UPDATE p SET p.[CustomerId] = a.[CustomerId]
      FROM [dbo].[MeetingParticipant] p
      INNER JOIN [dbo].[CustomerPortalAccount] a ON a.[UserId] = p.[UserId] AND a.[TenantId] = p.[TenantId]
      WHERE p.[CustomerId] IS NULL`;
  }

  // F4 (D-58 en calls): mismo backfill para CallParticipant.
  const pendingCalls = await prisma.$queryRaw<Array<{ total: number }>>`
    SELECT COUNT(*) AS total
    FROM [dbo].[CallParticipant] p
    INNER JOIN [dbo].[CustomerPortalAccount] a ON a.[UserId] = p.[UserId] AND a.[TenantId] = p.[TenantId]
    WHERE p.[CustomerId] IS NULL`;
  const callCandidates = Number(pendingCalls[0]?.total ?? 0);
  let callsUpdated = 0;
  if (!dryRun && callCandidates > 0) {
    callsUpdated = await prisma.$executeRaw`
      UPDATE p SET p.[CustomerId] = a.[CustomerId]
      FROM [dbo].[CallParticipant] p
      INNER JOIN [dbo].[CustomerPortalAccount] a ON a.[UserId] = p.[UserId] AND a.[TenantId] = p.[TenantId]
      WHERE p.[CustomerId] IS NULL`;
  }

  const report = { dryRun, candidates, updated, callCandidates, callsUpdated };
  logger.info(report, dryRun ? 'backfill-participant-customer-id: dry run' : 'backfill-participant-customer-id: applied');
  process.stdout.write(`${JSON.stringify(report)}\n`);
  await prisma.$disconnect();
}

main().catch((err: unknown) => {
  logger.error({ err }, 'backfill-participant-customer-id failed');
  process.exitCode = 1;
});
