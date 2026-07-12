import { mkdir } from 'node:fs/promises';
import { config } from './config.js';
import { logger } from './logger.js';
import { connectRabbit, disconnectRabbit } from './rabbit/rabbit-connection.js';
import { startConsumer } from './rabbit/consumer.js';
import { connectRedis, disconnectRedis, redis } from './redis/redis-client.js';
import { TranscriptInbox } from './redis/transcript-inbox.js';
import { ServiceTokenClient } from './auth/service-token-client.js';
import { CloudStorageClient } from './cloudstorage/cloudstorage-client.js';
import { processRecordingReady } from './pipeline.js';

async function main(): Promise<void> {
  await mkdir(config.whisper.tempDir, { recursive: true });
  await connectRedis();
  await connectRabbit();

  const tokens = new ServiceTokenClient();
  const cloudStorage = new CloudStorageClient(tokens);
  const inbox = new TranscriptInbox(redis);

  startConsumer((event) => processRecordingReady(event, { cloudStorage }), { inbox });

  logger.info({ service: config.serviceName, concurrency: config.concurrency }, 'communication-transcript-worker started');
}

async function shutdown(signal: NodeJS.Signals): Promise<void> {
  logger.info({ signal }, 'shutting down');
  try {
    await disconnectRabbit();
    await disconnectRedis();
  } catch (err) {
    logger.error({ err: (err as Error).message }, 'error during shutdown');
  } finally {
    process.exit(0);
  }
}

process.on('SIGTERM', () => void shutdown('SIGTERM'));
process.on('SIGINT', () => void shutdown('SIGINT'));

main().catch((err: unknown) => {
  logger.fatal({ err: (err as Error).message }, 'fatal startup error');
  process.exit(1);
});
