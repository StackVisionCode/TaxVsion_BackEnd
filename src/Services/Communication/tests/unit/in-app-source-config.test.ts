import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { config } from '../../src/infrastructure/config.js';

// Un flag de grupo que main.ts no pasa a ningún consumer parece hacer algo y no hace nada.
describe('config.inAppSource', () => {
  it('inAppSource only declares groups that main.ts wires', () => {
    const main = readFileSync(new URL('../../src/main.ts', import.meta.url), 'utf8');
    const unwired = Object.keys(config.inAppSource).filter(
      (group) => !main.includes(`config.inAppSource.${group}`),
    );

    expect(unwired).toEqual([]);
  });
});
