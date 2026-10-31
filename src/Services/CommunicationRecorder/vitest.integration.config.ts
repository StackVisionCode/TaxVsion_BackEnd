import { defineConfig } from 'vitest/config';

// Pruebas con ffmpeg real: fuera de `npm test` (requieren RECORDER_IT_FFMPEG/FFPROBE/FONT).
export default defineConfig({
  test: {
    include: ['tests/integration/**/*.test.ts'],
    exclude: ['node_modules', 'dist'],
    environment: 'node',
    testTimeout: 600_000,
    hookTimeout: 600_000,
  },
});
