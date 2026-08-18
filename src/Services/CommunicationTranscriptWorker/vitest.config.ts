import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['tests/**/*.test.ts', 'tests/**/*.spec.ts'],
    exclude: ['node_modules', 'dist'],
    environment: 'node',
    // config.ts valida process.env al importarse (fail-fast en prod). Los unit
    // tests que lo arrastran necesitan estas vars — dummy, nunca infra real.
    env: {
      TRANSCRIPT_WORKER_RABBITMQ_URI: 'amqp://localhost:5672',
      TRANSCRIPT_WORKER_REDIS_URI: 'redis://localhost:6379',
      TRANSCRIPT_WORKER_AUTH_BASE_URL: 'http://localhost:5124',
      TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_SECRET: 'test-client-secret',
      TRANSCRIPT_WORKER_CLOUDSTORAGE_BASE_URL: 'http://localhost:5330',
      TRANSCRIPT_WORKER_MINIO_ENDPOINT: 'localhost',
      TRANSCRIPT_WORKER_MINIO_ACCESS_KEY: 'test-access-key',
      TRANSCRIPT_WORKER_MINIO_SECRET_KEY: 'test-secret-key',
    },
  },
});
