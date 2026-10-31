import { connectPrisma, prisma } from '../infrastructure/persistence/prisma-client.js';
import { connectRedis, redis, redisPub, redisSub } from '../infrastructure/redis/redis-client.js';
import { buildContainer } from '../infrastructure/container.js';
import { logger } from '../infrastructure/logger/logger.js';
import { MeetingEndReason } from '../domain/meetings/meeting-enums.js';
import {
  endBySystemAndFinalize,
  expireStaleScheduledMeetings,
} from '../application/use-cases/meeting-lifecycle-sweep.js';

/**
 * Job one-shot (D-60): limpia las reuniones eternas pasando por el dominio, con los mismos
 * eventos que el scheduler. `Scheduled` vencidas → Expired; `Live` sin nadie dentro → Ended por
 * Backfill. Sin socket (no hay sala que avisar). `--dry-run` solo informa.
 *
 *   npx tsx src/jobs/expire-stale-meetings.ts --dry-run
 *   node dist/jobs/expire-stale-meetings.js
 */
async function main(): Promise<void> {
  const dryRun = process.argv.includes('--dry-run');
  await connectRedis();
  await connectPrisma();
  const container = buildContainer();
  const deps = { ...container, emitter: undefined };
  const now = new Date();
  const correlationId = `expire-stale-meetings:${now.toISOString()}`;

  const expiry = await expireStaleScheduledMeetings({ now, take: 5000, correlationId, dryRun }, deps);

  const live = await container.meetings.listLive({ take: 5000 });
  let liveEmpty = 0;
  for (const meeting of live) {
    if (meeting.emptySinceUtc() === null) continue;
    liveEmpty += 1;
    if (dryRun) continue;
    await endBySystemAndFinalize(meeting, MeetingEndReason.Backfill, now, correlationId, deps);
  }

  const report = {
    dryRun,
    scheduledChecked: expiry.checked,
    scheduledExpired: expiry.expired,
    liveChecked: live.length,
    liveEndedBackfill: liveEmpty,
  };
  logger.info(report, dryRun ? 'expire-stale-meetings: dry run' : 'expire-stale-meetings: applied');
  process.stdout.write(`${JSON.stringify(report)}\n`);

  await prisma.$disconnect();
  await Promise.all([redis.quit(), redisPub.quit(), redisSub.quit()]).catch(() => undefined);
}

main().catch((err: unknown) => {
  logger.error({ err }, 'expire-stale-meetings failed');
  process.exitCode = 1;
});
