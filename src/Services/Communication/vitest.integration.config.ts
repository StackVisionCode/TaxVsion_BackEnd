import { defineConfig } from 'vitest/config';

/**
 * Tests contra la infraestructura real del entorno local (SQL Server de `.env`). Fuera del run normal
 * y del CI (el runner no tiene base): `npm run test:integration`.
 */
export default defineConfig({
  test: {
    globals: false,
    include: ['tests/integration/**/*.test.ts'],
    environment: 'node',
    setupFiles: ['dotenv/config'],
    testTimeout: 60_000,
  },
});
