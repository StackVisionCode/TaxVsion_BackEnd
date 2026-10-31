import 'dotenv/config';
import { createAvatarSource } from './compose/avatars.js';
import { createKeyframeSizeProber } from './compose/normalize.js';
import { Redis } from 'ioredis';
import { createFfmpegCaptureLauncher } from './capture/ffmpeg-capture.js';
import { PortAllocator, udpPortProbe } from './capture/port-allocator.js';
import { SessionManager } from './capture/session-manager.js';
import { createFfmpegRunner, createProber } from './compose/ffmpeg-runner.js';
import { loadConfig } from './config.js';
import { buildServer } from './http/server.js';
import { createServiceAuthGuard, remoteJwks } from './http/service-auth.js';
import { createLogger } from './logger.js';
import { ComposeQueue } from './queue/compose-queue.js';
import { ComposeWorker } from './queue/compose-worker.js';
import { RabbitEventBus } from './rabbit/event-bus.js';
import { runJanitor } from './spool/janitor.js';
import { KeyedMutex, SpoolStore } from './spool/spool-store.js';
import { createMetrics } from './telemetry/metrics.js';
import { createMinioUploader } from './upload/object-uploader.js';

async function main(): Promise<void> {
  const config = loadConfig(process.env);
  const logger = createLogger(config.logLevel, config.serviceName, config.env);
  const metrics = createMetrics();

  const store = new SpoolStore(config.spoolDir);
  await store.init();
  const locks = new KeyedMutex();

  const redisOptions = {
    maxRetriesPerRequest: null,
    connectionName: config.serviceName,
    retryStrategy: (times: number) => Math.min(1000 * 2 ** (times - 1), 15_000),
  };
  const redis = new Redis(config.redis.uri, redisOptions);
  const blockingRedis = new Redis(config.redis.uri, { ...redisOptions, connectionName: `${config.serviceName}-blocking` });
  redis.on('error', (err: Error) => logger.error({ err: err.message }, 'Redis error'));
  blockingRedis.on('error', (err: Error) => logger.error({ err: err.message }, 'Redis (blocking) error'));
  const queue = new ComposeQueue(redis, blockingRedis, config.redis.keyPrefix);

  const bus = new RabbitEventBus(
    {
      uri: config.rabbitmq.uri,
      exchange: config.rabbitmq.exchange,
      externalUploadsQueue: config.rabbitmq.externalUploadsQueue,
    },
    logger,
  );
  await bus.connect();

  const sessions = new SessionManager({
    store,
    locks,
    jobs: queue,
    bus,
    ports: new PortAllocator(config.rtp.portMin, config.rtp.portMax, udpPortProbe(config.rtp.listenIp)),
    launcher: createFfmpegCaptureLauncher({
      ffmpegPath: config.ffmpeg.binPath,
      readyDelayMs: config.capture.readyDelayMs,
    }),
    metrics,
    logger,
    settings: {
      announcedIp: config.rtp.announcedIp,
      listenIp: config.rtp.listenIp,
      maxActiveSessions: config.maxActiveSessions,
      stopTimeoutMs: config.capture.stopTimeoutMs,
      orphanTimeoutMinutes: config.orphanTimeoutMinutes,
      retry: config.retry,
    },
  });
  await sessions.recoverOnBoot();

  const worker = new ComposeWorker({
    store,
    locks,
    queue,
    bus,
    uploader: createMinioUploader({
      endpoint: config.minio.endpoint,
      port: config.minio.port,
      useSSL: config.minio.useSSL,
      accessKey: config.minio.accessKey,
      secretKey: config.minio.secretKey,
      bucket: config.minio.tempBucket,
    }),
    runner: createFfmpegRunner({ ffmpegPath: config.ffmpeg.binPath, nice: config.compose.nice }),
    probe: createProber(config.ffmpeg.ffprobeBinPath),
    sizes: createKeyframeSizeProber(config.ffmpeg.ffprobeBinPath),
    metrics,
    logger,
    avatars: createAvatarSource({
      baseUrl: config.avatars.cloudStorageBaseUrl,
      timeoutMs: config.avatars.timeoutMs,
      maxBytes: config.avatars.maxBytes,
    }),
    settings: {
      fontFile: config.fontFile,
      threads: config.compose.threads,
      timeoutFactor: config.compose.timeoutFactor,
      timeoutMinMinutes: config.compose.timeoutMinMinutes,
      sourcePrefix: config.minio.sourcePrefix,
      externalUploadsQueue: config.rabbitmq.externalUploadsQueue,
      retry: config.retry,
    },
  });
  worker.start();

  const janitor = setInterval(() => {
    void runJanitor({ store, locks, logger }, new Date()).catch((err: unknown) =>
      logger.error({ err: (err as Error).message }, 'janitor failed'),
    );
  }, config.janitorIntervalMinutes * 60_000);
  janitor.unref();
  void runJanitor({ store, locks, logger }, new Date()).catch(() => undefined);

  const auth = createServiceAuthGuard({
    keys: remoteJwks(config.auth.jwksUri, config.auth.jwksCacheMaxAgeSeconds),
    issuer: config.auth.issuer,
    audience: config.auth.audience,
    allowedClientIds: config.auth.allowedClientIds,
  });
  const app = await buildServer({
    sessions,
    auth,
    metrics,
    queueDepth: () => queue.depth(),
    logger,
    bodyLimitBytes: config.http.bodyLimitBytes,
  });
  await app.listen({ port: config.http.port, host: config.http.host });
  logger.info({ port: config.http.port, spool: config.spoolDir }, 'communication-recorder started');

  let shuttingDown = false;
  const shutdown = async (signal: NodeJS.Signals) => {
    if (shuttingDown) return;
    shuttingDown = true;
    logger.info({ signal }, 'shutting down');
    try {
      clearInterval(janitor);
      await app.close();
      // Las capturas vivas se cierran con trailer; al volver se publicarán como RecorderRestarted.
      await sessions.shutdown();
      await worker.stop(config.shutdownDrainMs);
      await bus.close();
      redis.disconnect();
      blockingRedis.disconnect();
      process.exit(0);
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'error during shutdown');
      process.exit(1);
    }
  };
  for (const signal of ['SIGTERM', 'SIGINT'] as const) process.once(signal, (s) => void shutdown(s));
}

main().catch((err: unknown) => {
  console.error('fatal startup error:', (err as Error).message);
  process.exit(1);
});
