import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: false,
    include: ['tests/**/*.test.ts', 'tests/**/*.spec.ts'],
    exclude: ['node_modules', 'dist'],
    environment: 'node',
    // config.ts valida process.env al importarse (fail-fast en prod). Los unit
    // tests que lo arrastran necesitan estas 6 vars — dummy, nunca infra real.
    env: {
      COMMUNICATION_DB_CONNECTION: 'sqlserver://localhost:1433;database=test',
      COMMUNICATION_REDIS_URI: 'redis://localhost:6379',
      COMMUNICATION_RABBITMQ_URI: 'amqp://localhost:5672',
      COMMUNICATION_JWKS_URI: 'http://localhost:5124/auth/.well-known/jwks.json',
      COMMUNICATION_JOIN_TICKET_SECRET: 'test-join-ticket-secret-0000000000000000',
      COMMUNICATION_SERVICE_AUTH_CLIENT_SECRET: 'test-client-secret',
    },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html'],
      exclude: ['dist/**', 'tests/**', '**/*.config.*', 'prisma/**'],
    },
  },
});
