import { describe, expect, it } from 'vitest';
import { routeLabelOf } from '../../src/infrastructure/telemetry/metrics.js';

/** F11 (E-259): una URL inventada no crea series nuevas en /metrics. */
describe('routeLabelOf', () => {
  it('usa la ruta registrada (con parámetros, no ids)', () => {
    expect(routeLabelOf('/communication/meetings/:id')).toBe('/communication/meetings/:id');
  });

  it('sin ruta registrada (404) agrupa todo en "unmatched"', () => {
    expect(routeLabelOf(undefined)).toBe('unmatched');
  });
});
