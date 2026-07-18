import { mkdir } from 'node:fs/promises';
import type { Server } from 'node:http';
import { config } from './config.js';
import { logger } from './logger.js';
import { connectRabbit, disconnectRabbit } from './rabbit/rabbit-connection.js';
import { startConsumer, type ConsumerHandle } from './rabbit/consumer.js';
import { connectRedis, disconnectRedis, redis } from './redis/redis-client.js';
import { TranscriptInbox } from './redis/transcript-inbox.js';
import { ServiceTokenClient } from './auth/service-token-client.js';
import { CloudStorageClient } from './cloudstorage/cloudstorage-client.js';
import { processRecordingReady } from './pipeline.js';
import { startMetricsServer, stopMetricsServer } from './telemetry/metrics-server.js';

const SHUTDOWN_DRAIN_MS = 60_000;

async function main(): Promise<void> {
  await mkdir(config.whisper.tempDir, { recursive: true });
  await connectRedis();
  await connectRabbit();
  const metricsServer = await startMetricsServer(config.metrics.port);

  const tokens = new ServiceTokenClient();
  const cloudStorage = new CloudStorageClient(tokens);
  const inbox = new TranscriptInbox(redis);

  const consumer = await startConsumer((event) => processRecordingReady(event, { cloudStorage }), { inbox });

  logger.info({ service: config.serviceName, concurrency: config.concurrency }, 'communication-transcript-worker started');

  const signals: NodeJS.Signals[] = ['SIGTERM', 'SIGINT'];
  for (const signal of signals) {
    process.once(signal, () => void shutdown(signal, consumer, metricsServer));
  }
}

async function shutdown(signal: NodeJS.Signals, consumer: ConsumerHandle, metricsServer: Server): Promise<void> {
  logger.info({ signal }, 'shutting down (graceful drain up to 60s)');
  try {
    // Fase Transcript 4 — deja de consumir NUEVOS mensajes y espera hasta
    // SHUTDOWN_DRAIN_MS a que el/los job(s) en curso (download+ffmpeg+whisper
    // +upload, potencialmente con retries de por medio) terminen antes de
    // cerrar Rabbit/Redis. Antes: cierre inmediato sin importar si habia un
    // job corriendo.
    await consumer.stop(SHUTDOWN_DRAIN_MS);
    await stopMetricsServer(metricsServer);
    await disconnectRabbit();
    await disconnectRedis();
    logger.info('shutdown complete');
    process.exit(0);
  } catch (err) {
    logger.error({ err: (err as Error).message }, 'error during shutdown');
    process.exit(1);
  }
}

main().catch((err: unknown) => {
  logger.fatal({ err: (err as Error).message }, 'fatal startup error');
  process.exit(1);
});
